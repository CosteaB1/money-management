using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.EventHandlers;

/// <summary>
/// Keeps the pool side consistent when a pool-linked <see cref="Transaction"/>
/// is soft-deleted — the mirror of
/// <c>RemoveLoanPaymentOnTransactionDeletedHandler</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit event SURVIVES; only the link is dropped.</b> This is the one
/// place where the pools slice deliberately diverges from loans, which hard-
/// removes the payment row. A loan payment is pure bookkeeping between two
/// parties; a unit event is the record that somebody else's stake changed. If a
/// deletion silently un-issued units, a third party's share of the account would
/// move with no audit trail. So the ledger entry stays and
/// <c>PoolUnitEvent.ClearMovementTransaction()</c> nulls the transaction id.
/// </para>
/// <para>
/// For a <c>Distribution</c> that ALSO reverts <c>SettledOn</c> to null —
/// deliberately. Deleting the payment puts the cash back in the account, so the
/// payout is owed again; leaving it marked settled would let the same money be
/// distributed a second time.
/// </para>
/// <para>
/// <b>Flags, never auto-corrects.</b> When the deleted row belongs to a period
/// that has already been distributed, the units behind those payouts were struck
/// against a balance that just changed. Recomputing them here would silently
/// restate what people have been paid, so the handler logs a warning and leaves
/// the numbers alone for the user to judge.
/// </para>
/// <para>
/// Fast gate: every pool-synthesized money row is transfer-flagged, so
/// non-transfer deletions return without touching the database. The re-pricing
/// MARK is an <c>IsAdjustment</c> row that no unit event links, so it is
/// correctly ignored here too — and
/// <c>DeleteTransactionCommandHandler</c> refuses to delete it anyway.
/// </para>
/// <para>
/// Idempotent by design — <c>DeletePoolEventCommandHandler</c> also soft-deletes
/// the linked transaction, so this handler fires afterward, finds no matching
/// unit event, and no-ops without saving.
/// </para>
/// </remarks>
internal sealed class ClearPoolMovementOnTransactionDeletedHandler(
    IApplicationDbContext db,
    ILogger<ClearPoolMovementOnTransactionDeletedHandler> logger)
    : IDomainEventHandler<TransactionDeletedDomainEvent>
{
    public async Task Handle(TransactionDeletedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        if (!domainEvent.IsTransfer)
        {
            return;
        }

        List<PoolUnitEvent> unitEvents = await db.PoolUnitEvents
            .Where(e => e.MovementTransactionId == domainEvent.TransactionId)
            .ToListAsync(cancellationToken);

        if (unitEvents.Count == 0)
        {
            return;
        }

        foreach (PoolUnitEvent unitEvent in unitEvents)
        {
            await WarnIfAlreadyDistributedAsync(unitEvent, cancellationToken);

            bool wasSettledDistribution =
                unitEvent.Kind == PoolUnitEventKind.Distribution && unitEvent.SettledOn is not null;

            unitEvent.ClearMovementTransaction();

            if (wasSettledDistribution)
            {
                logger.LogWarning(
                    "Distribution {EventId} on pool {PoolId} is UNPAID again: its payment transaction "
                    + "{TransactionId} was deleted, so the cash is back in the account and the payout is owed.",
                    unitEvent.Id,
                    unitEvent.PoolId,
                    domainEvent.TransactionId);
            }
            else
            {
                logger.LogInformation(
                    "Cleared movement link on pool unit event {EventId} for soft-deleted transaction "
                    + "{TransactionId}. The units themselves are unchanged.",
                    unitEvent.Id,
                    domainEvent.TransactionId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task WarnIfAlreadyDistributedAsync(
        PoolUnitEvent unitEvent,
        CancellationToken cancellationToken)
    {
        DateOnly? latestDistribution = await db.PoolUnitEvents
            .Where(e => e.PoolId == unitEvent.PoolId
                && e.Kind == PoolUnitEventKind.Distribution
                && e.Id != unitEvent.Id)
            .Select(e => (DateOnly?)e.OccurredOn)
            .MaxAsync(cancellationToken);

        if (latestDistribution is DateOnly latest && unitEvent.OccurredOn <= latest)
        {
            logger.LogWarning(
                "Transaction {TransactionId} was deleted from under pool unit event {EventId} dated "
                + "{OccurredOn}, which sits in a period already distributed on {DistributionDate} "
                + "(pool {PoolId}). Units and payouts are NOT adjusted automatically - review them.",
                unitEvent.MovementTransactionId,
                unitEvent.Id,
                unitEvent.OccurredOn,
                latest,
                unitEvent.PoolId);
        }
    }
}
