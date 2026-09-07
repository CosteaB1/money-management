using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.RecordCostReimbursement;

internal sealed class RecordCostReimbursementCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock)
    : ICommandHandler<RecordCostReimbursementCommand, RecordCostReimbursementResponse>
{
    public async Task<Result<RecordCostReimbursementResponse>> Handle(
        RecordCostReimbursementCommand command,
        CancellationToken cancellationToken)
    {
        Result<PoolWriteContext> contextResult =
            await PoolWriteContext.LoadAsync(db, command.PoolId, clock, cancellationToken);

        if (contextResult.IsFailure)
        {
            return Result.Failure<RecordCostReimbursementResponse>(contextResult.Error);
        }

        PoolWriteContext context = contextResult.Value;

        // Optional mark. Skipped entirely when no value is supplied, which keeps
        // the literal promise of this command: no cash leg, no transaction.
        PoolMarkOutcome mark = new(null, 0m);
        if (command.PoolValueNow is decimal poolValueNow)
        {
            Result<PoolMarkOutcome> markResult = await PoolMark.WriteAsync(
                db,
                fxConverter,
                context.Account,
                context.DerivedBalance,
                poolValueNow,
                context.Today,
                cancellationToken);

            if (markResult.IsFailure)
            {
                return Result.Failure<RecordCostReimbursementResponse>(markResult.Error);
            }

            mark = markResult.Value;
        }

        decimal markedBalance = command.PoolValueNow ?? context.DerivedBalance;
        PoolSnapshot snapshot = context.SnapshotToday(markedBalance);

        Result<decimal> navResult = snapshot.RequireNav();
        if (navResult.IsFailure)
        {
            return Result.Failure<RecordCostReimbursementResponse>(navResult.Error);
        }

        decimal navPerUnit = navResult.Value;

        PoolPosition? owner = snapshot.Positions.FirstOrDefault(p => p.IsOwner);
        if (owner is null)
        {
            return Result.Failure<RecordCostReimbursementResponse>(PoolErrors.OwnerRequired);
        }

        List<PoolPosition> contributors =
        [
            .. snapshot.Positions.Where(p => !p.IsOwner && p.Units > PoolUnitEvent.UnitsDustTolerance),
        ];

        if (contributors.Count == 0)
        {
            return Result.Failure<RecordCostReimbursementResponse>(PoolErrors.CostNoOutsideUnits);
        }

        var lines = new List<CostShareLine>(contributors.Count);
        var events = new List<PoolUnitEvent>(contributors.Count + 1);

        decimal totalUnitsTransferred = 0m;
        decimal totalAmountRecovered = 0m;

        foreach (PoolPosition contributor in contributors)
        {
            // Their share of the BILL, by unit fraction. The owner keeps their
            // own share — they paid it, and it was theirs to pay.
            decimal amount = PoolUnitRegister.RoundMoney(command.Amount * contributor.OwnedFraction);
            if (amount <= 0m)
            {
                continue;
            }

            decimal units = PoolUnitRegister.RoundUnits(amount / navPerUnit);

            if (units - contributor.Units > PoolUnitEvent.UnitsDustTolerance)
            {
                return Result.Failure<RecordCostReimbursementResponse>(PoolErrors.CostExceedsParticipantUnits);
            }

            Result<PoolUnitEvent> shareResult = PoolUnitEvent.Create(
                context.Pool.Id,
                contributor.ParticipantId,
                PoolUnitEventKind.CostShare,
                context.Today,
                units,
                navPerUnit,
                PoolUnitRegister.RoundMoney(snapshot.PoolValue),
                // No cash and no settlement date: the money never entered the
                // account. PoolUnitEvent rejects both on this kind.
                cash: null,
                settledOn: null,
                command.Notes,
                context.Currency,
                context.Pool.InceptionDate,
                participantIsOwner: false,
                clock);

            if (shareResult.IsFailure)
            {
                return Result.Failure<RecordCostReimbursementResponse>(shareResult.Error);
            }

            events.Add(shareResult.Value);
            lines.Add(new CostShareLine(
                shareResult.Value.Id,
                contributor.ParticipantId,
                contributor.Name,
                units,
                amount));

            totalUnitsTransferred += units;
            totalAmountRecovered += amount;
        }

        if (totalUnitsTransferred <= 0m)
        {
            return Result.Failure<RecordCostReimbursementResponse>(PoolErrors.CostNoOutsideUnits);
        }

        Result<PoolUnitEvent> recoveryResult = PoolUnitEvent.Create(
            context.Pool.Id,
            owner.ParticipantId,
            PoolUnitEventKind.CostRecovery,
            context.Today,
            totalUnitsTransferred,
            navPerUnit,
            PoolUnitRegister.RoundMoney(snapshot.PoolValue),
            cash: null,
            settledOn: null,
            command.Notes,
            context.Currency,
            context.Pool.InceptionDate,
            participantIsOwner: true,
            clock);

        if (recoveryResult.IsFailure)
        {
            return Result.Failure<RecordCostReimbursementResponse>(recoveryResult.Error);
        }

        PoolUnitEvent recovery = recoveryResult.Value;
        events.Add(recovery);

        // The legs must net to zero before anything is saved. If they don't, the
        // pool's total units change and NAV moves — which would mean a cost
        // reimbursement had silently created or destroyed value. Cheap to check,
        // catastrophic to miss.
        decimal net = 0m;
        foreach (PoolUnitEvent unitEvent in events)
        {
            net += unitEvent.UnitsDelta;
        }

        if (Math.Abs(net) > PoolUnitEvent.UnitsDustTolerance)
        {
            return Result.Failure<RecordCostReimbursementResponse>(PoolErrors.CostLegsUnbalanced);
        }

        foreach (PoolUnitEvent unitEvent in events)
        {
            db.PoolUnitEvents.Add(unitEvent);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new RecordCostReimbursementResponse(
            navPerUnit,
            totalUnitsTransferred,
            totalAmountRecovered,
            mark.Delta,
            mark.TransactionId,
            recovery.Id,
            lines);
    }
}
