using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.RecordSubscription;

internal sealed class RecordSubscriptionCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<RecordSubscriptionCommand, RecordSubscriptionResponse>
{
    public async Task<Result<RecordSubscriptionResponse>> Handle(
        RecordSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        Result<PoolWriteContext> contextResult =
            await PoolWriteContext.LoadAsync(db, command.PoolId, clock, cancellationToken);

        if (contextResult.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(contextResult.Error);
        }

        PoolWriteContext context = contextResult.Value;

        Result<PoolParticipant> participantResult = context.RequireActiveParticipant(command.ParticipantId);
        if (participantResult.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(participantResult.Error);
        }

        PoolParticipant participant = participantResult.Value;

        Result cashGuard = context.GuardCashIsNotABalance(command.Cash, command.PoolValueNow);
        if (cashGuard.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(cashGuard.Error);
        }

        // STEP 1 — MARK. Before anything is priced. Reversing these two steps is
        // the single most expensive mistake available in this slice: it books the
        // subscriber's principal as trading profit and pays the other
        // participants a share of it.
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
            return Result.Failure<RecordSubscriptionResponse>(markResult.Error);
        }

        PoolMarkOutcome mark = markResult.Value;

        // STEP 2 — read V and U at the MARKED value. PoolValue subtracts any
        // closed-but-unpaid distribution still sitting in the account, so the
        // subscriber is not priced against money that is already owed away.
        PoolSnapshot snapshot = context.SnapshotToday(command.PoolValueNow);

        Result<decimal> navResult = snapshot.RequireNav();
        if (navResult.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(navResult.Error);
        }

        decimal navPerUnit = navResult.Value;

        // STEP 3 — mint at that NAV. Rounded to the storage scale first, so the
        // entity and the row Postgres keeps agree exactly.
        decimal units = PoolUnitRegister.RoundUnits(command.Cash / navPerUnit);

        Result<PoolUnitEvent> eventResult = PoolUnitEvent.Create(
            context.Pool.Id,
            participant.Id,
            PoolUnitEventKind.Subscription,
            context.Today,
            units,
            navPerUnit,
            PoolUnitRegister.RoundMoney(snapshot.PoolValue),
            new Money(command.Cash, context.Currency),
            // A subscription settles the moment it is recorded — the money is
            // in the account, which is how we could price it at all.
            settledOn: context.Today,
            command.Notes,
            context.Currency,
            context.Pool.InceptionDate,
            participant.IsOwner,
            clock);

        if (eventResult.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(eventResult.Error);
        }

        PoolUnitEvent unitEvent = eventResult.Value;

        // STEP 4 — the money row. Transfer-flagged so it stays out of P&L.
        (TransactionDirection direction, string description) = PoolMovements.ForSubscription(participant.Name);

        decimal? amountMdl = await fxConverter.ConvertAsync(
            command.Cash,
            context.Currency,
            ReportingCurrencies.Mdl,
            context.Today,
            cancellationToken);

        Result<Transaction> legResult = PoolMoneyLeg.Create(
            context.Account.Id,
            context.Today,
            direction,
            new Money(command.Cash, context.Currency),
            description,
            // The subscriber's own wallet is outside the app, so there is no
            // counter account to point at.
            counterAccountId: null,
            amountMdl,
            command.Notes);

        if (legResult.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(legResult.Error);
        }

        Transaction leg = legResult.Value;
        db.Transactions.Add(leg);

        Result link = unitEvent.LinkMovementTransaction(leg.Id);
        if (link.IsFailure)
        {
            return Result.Failure<RecordSubscriptionResponse>(link.Error);
        }

        db.PoolUnitEvents.Add(unitEvent);

        // ONE save: mark, unit event and money row land (or fail) together. A
        // mark that survived a failed unit event would silently become pure
        // trading profit.
        await db.SaveChangesAsync(cancellationToken);

        return new RecordSubscriptionResponse(
            unitEvent.Id,
            units,
            navPerUnit,
            unitEvent.PoolValuePreMoney,
            mark.Delta,
            mark.TransactionId,
            leg.Id);
    }
}
