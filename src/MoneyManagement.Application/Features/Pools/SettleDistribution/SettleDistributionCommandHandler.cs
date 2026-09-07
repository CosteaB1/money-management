using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.SettleDistribution;

internal sealed class SettleDistributionCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<SettleDistributionCommand, SettleDistributionResponse>
{
    public async Task<Result<SettleDistributionResponse>> Handle(
        SettleDistributionCommand command,
        CancellationToken cancellationToken)
    {
        Result<PoolWriteContext> contextResult =
            await PoolWriteContext.LoadAsync(db, command.PoolId, clock, cancellationToken);

        if (contextResult.IsFailure)
        {
            return Result.Failure<SettleDistributionResponse>(contextResult.Error);
        }

        PoolWriteContext context = contextResult.Value;

        // The event must belong to the pool named in the route — a valid event
        // id under the wrong pool is not-found, not cross-pool access.
        PoolUnitEvent? unitEvent = context.Events.FirstOrDefault(e => e.Id == command.EventId);
        if (unitEvent is null)
        {
            return Result.Failure<SettleDistributionResponse>(PoolErrors.UnitEventNotFound(command.EventId));
        }

        if (unitEvent.Kind != PoolUnitEventKind.Distribution)
        {
            return Result.Failure<SettleDistributionResponse>(PoolErrors.SettleNotApplicable);
        }

        if (unitEvent.Cash is not Money cash)
        {
            // Unreachable: PoolUnitEvent.Create requires cash on a distribution.
            return Result.Failure<SettleDistributionResponse>(PoolErrors.CashRequired);
        }

        PoolParticipant? participant = context.Participants
            .FirstOrDefault(p => p.Id == unitEvent.ParticipantId);

        if (participant is null)
        {
            return Result.Failure<SettleDistributionResponse>(
                PoolErrors.ParticipantNotFound(unitEvent.ParticipantId));
        }

        DateOnly settledOn = command.SettledOn ?? context.Today;

        (TransactionDirection direction, string description) = PoolMovements.ForDistribution(participant.Name);

        // FX at the day the money actually left, not at the close — the leg's
        // own date is the rate date on every write path in this codebase.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            cash.Amount,
            cash.Currency,
            ReportingCurrencies.Mdl,
            settledOn,
            cancellationToken);

        Result<Transaction> legResult = PoolMoneyLeg.Create(
            context.Account.Id,
            settledOn,
            direction,
            cash,
            description,
            // The friend's wallet is outside the app.
            counterAccountId: null,
            amountMdl,
            command.Notes ?? unitEvent.Notes);

        if (legResult.IsFailure)
        {
            return Result.Failure<SettleDistributionResponse>(legResult.Error);
        }

        Transaction leg = legResult.Value;

        // Settle sets SettledOn and the transaction id together, and rejects a
        // second settlement. Called BEFORE the row is added so a rejected
        // settlement leaves no orphan transaction in the change tracker.
        Result settle = unitEvent.Settle(settledOn, leg.Id, clock);
        if (settle.IsFailure)
        {
            return Result.Failure<SettleDistributionResponse>(settle.Error);
        }

        db.Transactions.Add(leg);
        await db.SaveChangesAsync(cancellationToken);

        return new SettleDistributionResponse(leg.Id, settledOn, cash.Amount);
    }
}
