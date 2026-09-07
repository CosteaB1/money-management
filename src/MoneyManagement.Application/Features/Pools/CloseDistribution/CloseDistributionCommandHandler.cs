using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.CloseDistribution;

internal sealed class CloseDistributionCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<CloseDistributionCommand, CloseDistributionResponse>
{
    public async Task<Result<CloseDistributionResponse>> Handle(
        CloseDistributionCommand command,
        CancellationToken cancellationToken)
    {
        Result<PoolWriteContext> contextResult =
            await PoolWriteContext.LoadAsync(db, command.PoolId, clock, cancellationToken);

        if (contextResult.IsFailure)
        {
            return Result.Failure<CloseDistributionResponse>(contextResult.Error);
        }

        PoolWriteContext context = contextResult.Value;

        // MARK FIRST. The close is the moment the month's P&L becomes real, so
        // this is the most important mark of the cycle.
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
            return Result.Failure<CloseDistributionResponse>(markResult.Error);
        }

        PoolMarkOutcome mark = markResult.Value;

        // Pool value here already excludes any EARLIER close that has not been
        // paid out yet, so last month's owed cash is not distributed a second
        // time.
        PoolSnapshot snapshot = context.SnapshotToday(command.PoolValueNow);

        Result<decimal> navResult = snapshot.RequireNav();
        if (navResult.IsFailure)
        {
            return Result.Failure<CloseDistributionResponse>(navResult.Error);
        }

        decimal navPerUnit = navResult.Value;
        decimal poolValuePreMoney = PoolUnitRegister.RoundMoney(snapshot.PoolValue);

        Result<List<ResolvedPayout>> payoutsResult = ResolvePayouts(command, context, snapshot);
        if (payoutsResult.IsFailure)
        {
            return Result.Failure<CloseDistributionResponse>(payoutsResult.Error);
        }

        List<ResolvedPayout> payouts = payoutsResult.Value;

        var lines = new List<DistributionLine>(payouts.Count);
        decimal totalCash = 0m;

        foreach (ResolvedPayout payout in payouts)
        {
            Result cashGuard = context.GuardCashIsNotABalance(payout.Cash, command.PoolValueNow);
            if (cashGuard.IsFailure)
            {
                return Result.Failure<CloseDistributionResponse>(cashGuard.Error);
            }

            // Every line is struck at the SAME NAV. Each one individually leaves
            // NAV unchanged, so the whole close does too — and pool value drops
            // by exactly the cash owed because the register subtracts unpaid
            // distributions.
            decimal units = PoolUnitRegister.RoundUnits(payout.Cash / navPerUnit);

            if (units - payout.Position.Units > PoolUnitEvent.UnitsDustTolerance)
            {
                return Result.Failure<CloseDistributionResponse>(PoolErrors.RedemptionExceedsStake);
            }

            Result<PoolUnitEvent> eventResult = PoolUnitEvent.Create(
                context.Pool.Id,
                payout.Position.ParticipantId,
                PoolUnitEventKind.Distribution,
                context.Today,
                units,
                navPerUnit,
                poolValuePreMoney,
                new Money(payout.Cash, context.Currency),
                // UNPAID. The one kind allowed to carry cash with a null
                // settlement date — and the reason the register has to subtract
                // it from pool value until the transfer actually goes out.
                settledOn: null,
                command.Notes,
                context.Currency,
                context.Pool.InceptionDate,
                payout.Position.IsOwner,
                clock);

            if (eventResult.IsFailure)
            {
                return Result.Failure<CloseDistributionResponse>(eventResult.Error);
            }

            PoolUnitEvent unitEvent = eventResult.Value;
            db.PoolUnitEvents.Add(unitEvent);

            totalCash += payout.Cash;
            lines.Add(new DistributionLine(
                unitEvent.Id,
                payout.Position.ParticipantId,
                payout.Position.Name,
                units,
                payout.Cash));
        }

        // ONE save: the mark and every unit event together. No transaction is
        // written here at all — that is phase two.
        await db.SaveChangesAsync(cancellationToken);

        return new CloseDistributionResponse(
            navPerUnit,
            poolValuePreMoney,
            mark.Delta,
            mark.TransactionId,
            totalCash,
            lines);
    }

    /// <summary>
    /// Turns the request into concrete (participant, cash) pairs, defaulting
    /// each to the participant's full distributable and refusing anything above
    /// it.
    /// </summary>
    private static Result<List<ResolvedPayout>> ResolvePayouts(
        CloseDistributionCommand command,
        PoolWriteContext context,
        PoolSnapshot snapshot)
    {
        var resolved = new List<ResolvedPayout>();

        if (command.Payouts is null || command.Payouts.Count == 0)
        {
            // The ordinary monthly close: every outside participant with profit
            // above their capital base, in full. The owner is deliberately
            // excluded — the owner's profit STAYS IN and grows their share; they
            // take value out with a redemption to Bybit when they want it.
            foreach (PoolPosition position in snapshot.Positions)
            {
                if (position.IsOwner || position.IsArchived)
                {
                    continue;
                }

                if (position.Distributable is decimal distributable && distributable > 0m)
                {
                    resolved.Add(new ResolvedPayout(position, distributable));
                }
            }

            return resolved.Count > 0
                ? Result.Success(resolved)
                : Result.Failure<List<ResolvedPayout>>(PoolErrors.DistributionNothingToPay);
        }

        foreach (DistributionPayout payout in command.Payouts)
        {
            Result<PoolParticipant> participantResult = context.RequireActiveParticipant(payout.ParticipantId);
            if (participantResult.IsFailure)
            {
                return Result.Failure<List<ResolvedPayout>>(participantResult.Error);
            }

            PoolPosition? position = snapshot.Find(payout.ParticipantId);
            if (position is null)
            {
                return Result.Failure<List<ResolvedPayout>>(PoolErrors.ParticipantNotFound(payout.ParticipantId));
            }

            if (position.Distributable is not decimal distributable)
            {
                return Result.Failure<List<ResolvedPayout>>(PoolErrors.NavUndefined);
            }

            decimal cash = payout.Cash ?? distributable;

            if (cash <= 0m)
            {
                return Result.Failure<List<ResolvedPayout>>(PoolErrors.CashMustBePositive);
            }

            // THE HIGH-WATER MARK, enforced. capitalBase never moves on a
            // distribution, so `stake - capitalBase` is permanently the amount
            // of NEW profit; below basis it is zero and a green month after a
            // drawdown correctly pays nothing.
            if (cash > distributable)
            {
                return Result.Failure<List<ResolvedPayout>>(PoolErrors.DistributionExceedsDistributable);
            }

            resolved.Add(new ResolvedPayout(position, cash));
        }

        return resolved;
    }

    private sealed record ResolvedPayout(PoolPosition Position, decimal Cash);
}
