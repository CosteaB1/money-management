using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.SettleDistribution;

/// <summary>
/// Phase two of a payout: <b>PAY.</b> Writes the real transfer-flagged Expense
/// row, dated the day the money actually left, and settles the unit event.
/// <para>
/// No mark and no re-pricing: the units were burned at the close and the cash
/// has been owed ever since. All this does is move the money from "owed and
/// still in the account" to "gone", which is exactly the transition
/// <c>PoolUnitRegister</c>'s unpaid-distribution subtraction is tracking.
/// </para>
/// <para>
/// <c>PoolUnitEvent.Settle</c> sets the date and the transaction TOGETHER, and
/// <c>LinkMovementTransaction</c> refuses distributions outright — a
/// distribution with a transaction but no settlement date still reads as unpaid
/// and would be counted out of the pool twice.
/// </para>
/// </summary>
/// <param name="PoolId">The pool. The event must belong to it.</param>
/// <param name="EventId">The distribution unit event returned by the close.</param>
/// <param name="SettledOn">
/// The day the transfer physically left. Defaults to today. Cannot precede the
/// close and cannot be in the future.
/// </param>
/// <param name="Notes">Free text for the payment row; falls back to the event's own notes.</param>
public sealed record SettleDistributionCommand(
    Guid PoolId,
    Guid EventId,
    DateOnly? SettledOn,
    string? Notes) : ICommand<SettleDistributionResponse>;

/// <param name="TransactionId">The payment row written on the pooled account.</param>
/// <param name="SettledOn">The date it was dated.</param>
/// <param name="Cash">The amount paid.</param>
public sealed record SettleDistributionResponse(Guid TransactionId, DateOnly SettledOn, decimal Cash);
