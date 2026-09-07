using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.DeletePoolEvent;

internal sealed class DeletePoolEventCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    ILogger<DeletePoolEventCommandHandler> logger) : ICommandHandler<DeletePoolEventCommand>
{
    public async Task<Result> Handle(DeletePoolEventCommand command, CancellationToken cancellationToken)
    {
        // The event must belong to the pool named in the route — a valid event
        // id under the wrong pool is treated as not-found rather than leaking
        // cross-pool access (the loans-slice convention).
        PoolUnitEvent? unitEvent = await db.PoolUnitEvents
            .FirstOrDefaultAsync(
                e => e.Id == command.EventId && e.PoolId == command.PoolId,
                cancellationToken);

        if (unitEvent is null)
        {
            return Result.Failure(PoolErrors.UnitEventNotFound(command.EventId));
        }

        if (unitEvent.Kind == PoolUnitEventKind.Seed)
        {
            return Result.Failure(PoolErrors.SeedCannotBeDeleted);
        }

        List<PoolUnitEvent> toRemove = [unitEvent];

        if (unitEvent.Kind is PoolUnitEventKind.CostShare or PoolUnitEventKind.CostRecovery)
        {
            // Half a cost transfer is worse than none: total units would change
            // and NAV with it. Remove the whole same-day batch so the ledger
            // stays balanced.
            List<PoolUnitEvent> batch = await db.PoolUnitEvents
                .Where(e => e.PoolId == command.PoolId
                    && e.OccurredOn == unitEvent.OccurredOn
                    && (e.Kind == PoolUnitEventKind.CostShare || e.Kind == PoolUnitEventKind.CostRecovery)
                    && e.Id != unitEvent.Id)
                .ToListAsync(cancellationToken);

            toRemove.AddRange(batch);
        }

        await WarnIfAlreadyDistributedAsync(command.PoolId, unitEvent, cancellationToken);

        foreach (PoolUnitEvent removing in toRemove)
        {
            await SoftDeleteMovementAsync(removing, cancellationToken);
        }

        // Hard-remove the ledger rows. The TransactionDeleted events raised
        // above re-run ClearPoolMovementOnTransactionDeletedHandler after save;
        // it finds no matching unit event and no-ops (idempotent by design).
        db.PoolUnitEvents.RemoveRange(toRemove);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Soft-deletes the event's money row, and the reciprocal leg of a two-leg
    /// redemption alongside it.
    /// </summary>
    private async Task SoftDeleteMovementAsync(PoolUnitEvent unitEvent, CancellationToken cancellationToken)
    {
        if (unitEvent.MovementTransactionId is not Guid transactionId)
        {
            return;
        }

        // Normal filtered set: an already soft-deleted transaction is invisible
        // here, which is exactly the skip path.
        Transaction? transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        if (transaction is null)
        {
            return;
        }

        Guid? counterAccountId = transaction.CounterAccountId;

        await MarkDeletedAsync(transaction, cancellationToken);

        if (counterAccountId is not Guid counterId)
        {
            return;
        }

        // Two-leg redemption: the leg on the destination account was written by
        // this slice in the same save, so removing only the pool side would
        // leave the destination permanently credited with money that never
        // arrived. Matched on the exact tuple both legs were built from, and
        // only removed when the match is unambiguous.
        TransactionDirection oppositeDirection = transaction.Direction == TransactionDirection.Income
            ? TransactionDirection.Expense
            : TransactionDirection.Income;

        List<Transaction> candidates = await db.Transactions
            .Where(t => t.AccountId == counterId
                && t.CounterAccountId == transaction.AccountId
                && t.TransactionDate == transaction.TransactionDate
                && t.Direction == oppositeDirection
                && t.Amount.Amount == transaction.Amount.Amount)
            .ToListAsync(cancellationToken);

        if (candidates.Count != 1)
        {
            logger.LogWarning(
                "Pool unit event {EventId}: found {Count} candidate counter legs on account {AccountId}; "
                + "leaving them alone. Remove the counter leg manually if it is stale.",
                unitEvent.Id,
                candidates.Count,
                counterId);
            return;
        }

        await MarkDeletedAsync(candidates[0], cancellationToken);
    }

    private async Task MarkDeletedAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        // FX-convert at the row's own date so downstream event consumers see the
        // same MDL value the create path booked — mirrors
        // DeleteTransactionCommandHandler.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            transaction.Amount.Amount,
            transaction.Amount.Currency,
            ReportingCurrencies.Mdl,
            transaction.TransactionDate,
            cancellationToken);

        transaction.MarkDeleted(amountMdl);
    }

    /// <summary>
    /// Flags — never silently repairs — a deletion that re-prices a period
    /// already distributed. The payouts have been struck and possibly paid; only
    /// the user can decide whether to claw one back, so this logs and continues.
    /// </summary>
    private async Task WarnIfAlreadyDistributedAsync(
        Guid poolId,
        PoolUnitEvent unitEvent,
        CancellationToken cancellationToken)
    {
        DateOnly? latestDistribution = await db.PoolUnitEvents
            .Where(e => e.PoolId == poolId
                && e.Kind == PoolUnitEventKind.Distribution
                && e.Id != unitEvent.Id)
            .Select(e => (DateOnly?)e.OccurredOn)
            .MaxAsync(cancellationToken);

        if (latestDistribution is DateOnly latest && unitEvent.OccurredOn <= latest)
        {
            logger.LogWarning(
                "Deleting pool unit event {EventId} dated {OccurredOn} re-prices a period already distributed "
                + "on {DistributionDate} (pool {PoolId}). Units already paid out are NOT adjusted automatically - "
                + "review the affected distributions.",
                unitEvent.Id,
                unitEvent.OccurredOn,
                latest,
                poolId);
        }
    }
}
