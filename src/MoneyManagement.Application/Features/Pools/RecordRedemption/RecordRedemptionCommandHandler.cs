using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.RecordRedemption;

internal sealed class RecordRedemptionCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<RecordRedemptionCommand, RecordRedemptionResponse>
{
    public async Task<Result<RecordRedemptionResponse>> Handle(
        RecordRedemptionCommand command,
        CancellationToken cancellationToken)
    {
        Result<PoolWriteContext> contextResult =
            await PoolWriteContext.LoadAsync(db, command.PoolId, clock, cancellationToken);

        if (contextResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(contextResult.Error);
        }

        PoolWriteContext context = contextResult.Value;

        Result<PoolParticipant> participantResult = context.RequireActiveParticipant(command.ParticipantId);
        if (participantResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(participantResult.Error);
        }

        PoolParticipant participant = participantResult.Value;

        // The looks-like-a-balance guard is relaxed ONLY when the caller has
        // explicitly said this is the pool's final redemption. It is not taken
        // on trust: the claim is verified against the ledger below, once the
        // units are known.
        Result cashGuard = context.GuardCashIsNotABalance(
            command.Cash,
            command.PoolValueNow,
            allowFullWindDown: command.IsFullWindDown);

        if (cashGuard.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(cashGuard.Error);
        }

        Result<Account?> destinationResult =
            await ResolveDestinationAsync(command, participant, context, cancellationToken);

        if (destinationResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(destinationResult.Error);
        }

        Account? destination = destinationResult.Value;

        // MARK FIRST, then price. Same ordering rule as a subscription.
        Result<PoolMarkOutcome> markResult = await PoolMark.WriteAsync(
            db,
            fxConverter,
            context.Account,
            context.DerivedBalance,
            command.PoolValueNow,
            context.Today,
            cancellationToken);

        if (markResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(markResult.Error);
        }

        PoolMarkOutcome mark = markResult.Value;

        PoolSnapshot snapshot = context.SnapshotToday(command.PoolValueNow);

        Result<decimal> navResult = snapshot.RequireNav();
        if (navResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(navResult.Error);
        }

        decimal navPerUnit = navResult.Value;
        decimal units = PoolUnitRegister.RoundUnits(command.Cash / navPerUnit);

        // Overdraw invariant — the pool's equivalent of
        // LoanErrors.PaymentExceedsOutstanding. Without it a participant can be
        // driven to negative units, which makes somebody else's fraction exceed
        // 1.0 and the owner's share of the account larger than the account.
        decimal held = snapshot.Find(participant.Id)?.Units ?? 0m;
        if (units - held > PoolUnitEvent.UnitsDustTolerance)
        {
            return Result.Failure<RecordRedemptionResponse>(PoolErrors.RedemptionExceedsStake);
        }

        // THE WIND-DOWN CLAIM IS VERIFIED, NEVER TRUSTED. `IsFullWindDown` bought
        // its way past the looks-like-a-balance guard, so it has to be an
        // assertion the ledger can refute: after this event the pool must hold
        // no units at all. Without this the flag would be a plain bypass switch,
        // and the demonstrated slip — a BALANCE typed into an AMOUNT field —
        // would be one checkbox away from landing again.
        //
        // Checked here rather than up front because it needs the NAV, which only
        // exists once the account has been marked. Sits beside the overdraw
        // invariant deliberately: both are "does the ledger support this?"
        // questions, and both answer before anything is saved.
        if (command.IsFullWindDown
            && snapshot.TotalUnits - units > PoolUnitEvent.UnitsDustTolerance)
        {
            return Result.Failure<RecordRedemptionResponse>(PoolErrors.NotAFullWindDown);
        }

        Result<PoolUnitEvent> eventResult = PoolUnitEvent.Create(
            context.Pool.Id,
            participant.Id,
            PoolUnitEventKind.Redemption,
            context.Today,
            units,
            navPerUnit,
            PoolUnitRegister.RoundMoney(snapshot.PoolValue),
            new Money(command.Cash, context.Currency),
            settledOn: context.Today,
            command.Notes,
            context.Currency,
            context.Pool.InceptionDate,
            participant.IsOwner,
            clock);

        if (eventResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(eventResult.Error);
        }

        PoolUnitEvent unitEvent = eventResult.Value;

        (TransactionDirection direction, string description) = PoolMovements.ForRedemption(participant.Name);

        // Converted ONCE, from the pool side, so a two-leg redemption conserves
        // value in the reporting currency — the CreateTransferCommandHandler
        // contract. The destination is same-currency by construction (see
        // ResolveDestinationAsync), so one rate serves both legs.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            command.Cash,
            context.Currency,
            ReportingCurrencies.Mdl,
            context.Today,
            cancellationToken);

        var money = new Money(command.Cash, context.Currency);

        Result<Transaction> legResult = PoolMoneyLeg.Create(
            context.Account.Id,
            context.Today,
            direction,
            money,
            description,
            counterAccountId: destination?.Id,
            amountMdl,
            command.Notes);

        if (legResult.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(legResult.Error);
        }

        Transaction leg = legResult.Value;
        db.Transactions.Add(leg);

        Transaction? counterLeg = null;
        if (destination is not null)
        {
            // The reciprocal leg. Without it, moving 400 from the pool to Bybit
            // would read as 400 of net worth simply disappearing.
            Result<Transaction> counterResult = PoolMoneyLeg.Create(
                destination.Id,
                context.Today,
                TransactionDirection.Income,
                new Money(command.Cash, destination.Balance.Currency),
                description,
                counterAccountId: context.Account.Id,
                amountMdl,
                command.Notes);

            if (counterResult.IsFailure)
            {
                return Result.Failure<RecordRedemptionResponse>(counterResult.Error);
            }

            counterLeg = counterResult.Value;
            db.Transactions.Add(counterLeg);
        }

        // The unit event links the POOL-side leg only: that is the row whose
        // deletion would re-price units.
        Result link = unitEvent.LinkMovementTransaction(leg.Id);
        if (link.IsFailure)
        {
            return Result.Failure<RecordRedemptionResponse>(link.Error);
        }

        db.PoolUnitEvents.Add(unitEvent);

        await db.SaveChangesAsync(cancellationToken);

        return new RecordRedemptionResponse(
            unitEvent.Id,
            units,
            navPerUnit,
            unitEvent.PoolValuePreMoney,
            mark.Delta,
            mark.TransactionId,
            leg.Id,
            counterLeg?.Id);
    }

    /// <summary>
    /// Validates the optional destination account. Null in, null out — that is
    /// the one-leg path.
    /// </summary>
    private async Task<Result<Account?>> ResolveDestinationAsync(
        RecordRedemptionCommand command,
        PoolParticipant participant,
        PoolWriteContext context,
        CancellationToken cancellationToken)
    {
        if (command.DestinationAccountId is not Guid destinationId)
        {
            return Result.Success<Account?>(null);
        }

        // OWNER ONLY, and this is the whole point of the two-leg path. The
        // pool-side leg is excluded from the owner's figures by the ownership
        // fraction, but the counter leg lands on a WHOLLY-OWNED account, where
        // it reads as the user's own contribution and its balance counts in full
        // towards net worth. For the owner that is exactly right — value moving
        // from the pool to Bybit is still theirs (POOLED-CAPITAL.md §6, Sep 18).
        // For anybody else it silently converts a friend's capital into the
        // user's on the way out. A friend's payout leaves the tracked world:
        // one leg, no counter account.
        //
        // Checked before the account is even loaded — this is a question about
        // WHO is redeeming, not about which account was named.
        if (!participant.IsOwner)
        {
            return Result.Failure<Account?>(PoolErrors.DestinationRequiresOwner);
        }

        if (destinationId == context.Account.Id)
        {
            return Result.Failure<Account?>(TransferErrors.SameSourceAndDestination);
        }

        Account? destination = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == destinationId && !a.IsArchived, cancellationToken);

        if (destination is null)
        {
            return Result.Failure<Account?>(TransferErrors.DestinationAccountNotFound(destinationId));
        }

        // Same-currency only. The pool never does FX (that is why Pool.Currency
        // must equal the account's), and a cross-currency leg would need a
        // separate destination amount that has nothing to do with the units
        // burned. Move to a same-currency account, or redeem one-leg and record
        // the arrival separately.
        if (!string.Equals(destination.Balance.Currency, context.Currency, StringComparison.Ordinal))
        {
            return Result.Failure<Account?>(PoolErrors.DestinationCurrencyMismatch);
        }

        // A pooled destination would need its own subscription to account for
        // the arriving units; silently crediting it would hand the money to that
        // pool's participants.
        if (await PooledAccountGuard.IsPooledAsync(db, destinationId, cancellationToken))
        {
            return Result.Failure<Account?>(PoolErrors.TransferBlocked);
        }

        return Result.Success<Account?>(destination);
    }
}
