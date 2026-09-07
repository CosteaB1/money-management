using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.CreatePool;

internal sealed class CreatePoolCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<CreatePoolCommand, CreatePoolResponse>
{
    public async Task<Result<CreatePoolResponse>> Handle(
        CreatePoolCommand command,
        CancellationToken cancellationToken)
    {
        // Archived accounts can't take new movements; archived and missing ids
        // collapse into the same NotFound (the loans-slice convention).
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.AccountId && !a.IsArchived, cancellationToken);

        if (account is null)
        {
            return Result.Failure<CreatePoolResponse>(AccountErrors.NotFound(command.AccountId));
        }

        // An account that cannot take an Adjustment can never be re-priced, so
        // its NAV would freeze at inception and every later subscription would
        // be struck at a stale price. Reading the eligibility set straight off
        // AdjustBalanceCommandHandler keeps the two definitions from drifting.
        if (!AdjustBalanceCommandHandler.EligibleTypes.Contains(account.Type))
        {
            return Result.Failure<CreatePoolResponse>(PoolErrors.AccountTypeNotEligible);
        }

        // IgnoreQueryFilters: an ARCHIVED pool still holds the account (the
        // unique index is deliberately unfiltered), because its unit history
        // still shapes the owner fraction for the dates it covered. A second
        // pool would make "the owner's share of this account" ambiguous for the
        // overlapping period.
        bool alreadyPooled = await db.Pools
            .IgnoreQueryFilters()
            .AnyAsync(p => p.AccountId == command.AccountId, cancellationToken);

        if (alreadyPooled)
        {
            return Result.Failure<CreatePoolResponse>(PoolErrors.AccountAlreadyPooled);
        }

        // The mirror of CreateGoal/UpdateGoal's guard, which only ever asked the
        // question in one direction: goal-then-pool was as open as pool-then-goal
        // was closed. SavingsGoal.Saved IS this account's derived balance -
        // GetGoals and GetGoalDetail apply no ownership fraction anywhere, and
        // savings goals are deliberately NOT on the read-side divergence list -
        // so from the moment the pool exists the goal would count the outside
        // participants' capital as the user's own progress and could report
        // itself complete on their money.
        //
        // IgnoreQueryFilters: an ARCHIVED goal still points at the account and
        // can be unarchived at any time, at which point it would read the pooled
        // balance. Unlink it first, or pool a different account.
        bool hasLinkedGoal = await db.SavingsGoals
            .IgnoreQueryFilters()
            .AnyAsync(g => g.LinkedAccountId == command.AccountId, cancellationToken);

        if (hasLinkedGoal)
        {
            return Result.Failure<CreatePoolResponse>(PoolErrors.GoalLinkBlocked);
        }

        Result<Pool> poolResult = Pool.Create(
            command.AccountId,
            command.Name,
            command.Currency,
            account.Balance.Currency,
            command.InceptionDate,
            command.Notes,
            clock);

        if (poolResult.IsFailure)
        {
            return Result.Failure<CreatePoolResponse>(poolResult.Error);
        }

        Pool pool = poolResult.Value;

        Result<PoolParticipant> ownerResult = PoolParticipant.Create(
            pool.Id,
            command.OwnerName,
            isOwner: true,
            joinedOn: command.InceptionDate,
            pool.InceptionDate,
            clock);

        if (ownerResult.IsFailure)
        {
            return Result.Failure<CreatePoolResponse>(ownerResult.Error);
        }

        PoolParticipant owner = ownerResult.Value;

        // As-of balance, NOT the date-blind sum AdjustBalance uses. That is
        // precisely why creation may back-date and AdjustBalance may not: rows
        // dated after inception stay outside the catch-up delta, so the mark
        // corrects the balance on the inception date without corrupting today's.
        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(db, cancellationToken);
        decimal derivedAtInception = ledger.NativeBalanceAsOf(account, command.InceptionDate);

        decimal poolValueAtInception = command.PoolValueAtInception ?? derivedAtInception;
        if (poolValueAtInception <= 0m)
        {
            return Result.Failure<CreatePoolResponse>(PoolErrors.PoolValueMustBePositive);
        }

        Result<PoolMarkOutcome> markResult = await PoolMark.WriteAsync(
            db,
            fxConverter,
            account,
            derivedAtInception,
            poolValueAtInception,
            command.InceptionDate,
            cancellationToken);

        if (markResult.IsFailure)
        {
            return Result.Failure<CreatePoolResponse>(markResult.Error);
        }

        PoolMarkOutcome mark = markResult.Value;

        // The seed carries NO cash and NO transaction: the owner's existing
        // balance simply BECOMES units at a NAV of exactly 1. Synthesizing an
        // Income leg to "fund" it would double the account.
        decimal seedUnits = PoolUnitRegister.RoundUnits(poolValueAtInception);

        Result<PoolUnitEvent> seedResult = PoolUnitEvent.Create(
            pool.Id,
            owner.Id,
            PoolUnitEventKind.Seed,
            command.InceptionDate,
            seedUnits,
            navPerUnit: 1m,
            poolValuePreMoney: 0m,
            cash: null,
            settledOn: null,
            notes: null,
            pool.Currency,
            pool.InceptionDate,
            participantIsOwner: true,
            clock);

        if (seedResult.IsFailure)
        {
            return Result.Failure<CreatePoolResponse>(seedResult.Error);
        }

        var participants = new List<PoolParticipant> { owner };
        var events = new List<PoolUnitEvent> { seedResult.Value };
        var transactions = new List<Transaction>();
        var unitsByParticipant = new Dictionary<Guid, decimal> { [owner.Id] = seedUnits };

        decimal runningUnits = seedUnits;

        // Replayed oldest-first: each backfill is priced against the units
        // outstanding at that moment, so two friends who arrived on different
        // days are struck at different NAVs — which is the entire point of
        // units over a fixed percentage.
        IEnumerable<BackdatedSubscription> backfills =
            (command.BackdatedSubscriptions ?? []).OrderBy(s => s.OccurredOn);

        foreach (BackdatedSubscription backfill in backfills)
        {
            Result<PoolParticipant> participantResult = PoolParticipant.Create(
                pool.Id,
                backfill.ParticipantName,
                isOwner: false,
                backfill.OccurredOn,
                pool.InceptionDate,
                clock);

            if (participantResult.IsFailure)
            {
                return Result.Failure<CreatePoolResponse>(participantResult.Error);
            }

            PoolParticipant participant = participantResult.Value;

            if (backfill.PoolValuePreMoney <= 0m)
            {
                return Result.Failure<CreatePoolResponse>(PoolErrors.PoolValueMustBePositive);
            }

            if (runningUnits <= PoolUnitEvent.UnitsDustTolerance)
            {
                return Result.Failure<CreatePoolResponse>(PoolErrors.NavUndefined);
            }

            decimal navPerUnit = PoolUnitRegister.RoundUnits(backfill.PoolValuePreMoney / runningUnits);

            // The two guards above are NOT enough between them. RoundUnits
            // quantizes to 12dp, so a pre-money value tiny against the units
            // outstanding - any ratio below 5e-13, which is what a near-worthless
            // pool holding a large unit count looks like - rounds the price to
            // exactly ZERO, and the very next line divides by it:
            // DivideByZeroException, an unhandled 500 raised by a user-supplied
            // number.
            //
            // Same rule and same error as SnapshotAsOf's "no units => no price,
            // and no VALUE => no price either". A price that quantizes to zero is
            // a zero price, not a small one, and it divides just as badly.
            if (navPerUnit <= 0m)
            {
                return Result.Failure<CreatePoolResponse>(PoolErrors.NavUndefined);
            }

            decimal units = PoolUnitRegister.RoundUnits(backfill.Cash / navPerUnit);

            Result<PoolUnitEvent> eventResult = PoolUnitEvent.Create(
                pool.Id,
                participant.Id,
                PoolUnitEventKind.Subscription,
                backfill.OccurredOn,
                units,
                navPerUnit,
                PoolUnitRegister.RoundMoney(backfill.PoolValuePreMoney),
                new Money(backfill.Cash, pool.Currency),
                // A subscription settles the moment it is recorded: the money
                // arrived on the day it arrived.
                settledOn: backfill.OccurredOn,
                backfill.Notes,
                pool.Currency,
                pool.InceptionDate,
                participantIsOwner: false,
                clock);

            if (eventResult.IsFailure)
            {
                return Result.Failure<CreatePoolResponse>(eventResult.Error);
            }

            PoolUnitEvent unitEvent = eventResult.Value;

            if (backfill.WriteMovementTransaction)
            {
                (TransactionDirection direction, string description) =
                    PoolMovements.ForSubscription(participant.Name);

                decimal? amountMdl = await fxConverter.ConvertAsync(
                    backfill.Cash,
                    pool.Currency,
                    ReportingCurrencies.Mdl,
                    backfill.OccurredOn,
                    cancellationToken);

                Result<Transaction> legResult = PoolMoneyLeg.Create(
                    account.Id,
                    backfill.OccurredOn,
                    direction,
                    new Money(backfill.Cash, pool.Currency),
                    description,
                    counterAccountId: null,
                    amountMdl,
                    backfill.Notes);

                if (legResult.IsFailure)
                {
                    return Result.Failure<CreatePoolResponse>(legResult.Error);
                }

                Transaction leg = legResult.Value;
                transactions.Add(leg);

                Result link = unitEvent.LinkMovementTransaction(leg.Id);
                if (link.IsFailure)
                {
                    return Result.Failure<CreatePoolResponse>(link.Error);
                }
            }

            participants.Add(participant);
            events.Add(unitEvent);
            unitsByParticipant[participant.Id] = units;
            runningUnits += units;
        }

        db.Pools.Add(pool);
        foreach (PoolParticipant participant in participants)
        {
            db.PoolParticipants.Add(participant);
        }

        foreach (PoolUnitEvent unitEvent in events)
        {
            db.PoolUnitEvents.Add(unitEvent);
        }

        foreach (Transaction transaction in transactions)
        {
            db.Transactions.Add(transaction);
        }

        // ONE save: pool, owner, seed, every backfilled participant/event and
        // the catch-up mark land (or fail) together. A partially-created pool
        // would have an undefined NAV and no way to repair it.
        await db.SaveChangesAsync(cancellationToken);

        IReadOnlyList<CreatedPoolParticipant> created =
        [
            .. participants.Select(p => new CreatedPoolParticipant(
                p.Id,
                p.Name,
                p.IsOwner,
                unitsByParticipant.GetValueOrDefault(p.Id))),
        ];

        return new CreatePoolResponse(
            pool.Id,
            owner.Id,
            seedUnits,
            mark.Delta,
            mark.TransactionId,
            created);
    }
}
