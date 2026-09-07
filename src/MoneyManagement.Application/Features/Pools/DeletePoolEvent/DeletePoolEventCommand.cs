using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.DeletePoolEvent;

/// <summary>
/// Removes a unit event (hard delete — the ledger row is bookkeeping, not a
/// financial event in itself) and soft-deletes its linked transaction, so the
/// account balance rolls back with it. Mirrors
/// <c>DeleteLoanPaymentCommandHandler</c>.
/// <para>
/// <b>This is the sanctioned undo.</b> Deleting the transaction from the
/// transactions page is blocked (<c>pools.delete_reprices_units</c>) precisely
/// so that money and units can only ever be removed together.
/// </para>
/// <para>
/// Two carve-outs, both to stop a half-removal from silently reassigning
/// somebody's stake:
/// <list type="bullet">
/// <item>The <c>Seed</c> cannot be deleted — it is the basis every later NAV is
/// denominated against. Archive the pool instead.</item>
/// <item>Deleting one half of a cost reimbursement would leave total units
/// changed and NAV moved, so a <c>CostShare</c>/<c>CostRecovery</c> deletion
/// removes that whole day's cost batch for the pool. The pair is one atomic
/// unit transfer and is undone as one.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="PoolId">The owning pool. An event under a different pool is not-found.</param>
/// <param name="EventId">The unit event to remove.</param>
public sealed record DeletePoolEventCommand(Guid PoolId, Guid EventId) : ICommand;
