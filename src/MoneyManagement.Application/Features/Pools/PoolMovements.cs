using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Maps a pool unit event to the shape of the transaction synthesized for it —
/// the pools counterpart of <see cref="Loans.LoanMovements"/>, so the
/// direction/description matrix lives in exactly one place:
/// <list type="bullet">
///   <item><description>Subscription → Income, "Pool subscription from {participant}"</description></item>
///   <item><description>Redemption → Expense, "Pool redemption to {participant}"</description></item>
///   <item><description>Distribution (settle) → Expense, "Pool payout to {participant}"</description></item>
///   <item><description>Redemption counter leg → Income on the destination account, same description</description></item>
/// </list>
/// Every synthesized money row follows the loan convention exactly:
/// <c>IsTransfer = true</c>, <c>IsAdjustment = false</c>,
/// <c>CategoryId = <see cref="SeededCategories.PoolId"/></c>,
/// <c>Source = Manual</c>, and <c>AmountMdl</c> FX-converted at the movement's
/// OWN date. Transfer-flagged keeps them out of every income/expense aggregate
/// (dashboard, reports, budget handlers) with zero changes — the Phase-4
/// Investment/Withdrawal precedent.
/// <para>
/// <b>The re-pricing MARK is not in this matrix on purpose.</b> It is a genuine
/// balance adjustment and must be indistinguishable from a hand-typed snapshot:
/// <c>IsAdjustment = true</c>, <c>IsTransfer = false</c>,
/// <see cref="SeededCategories.BalanceAdjustmentId"/> and the same
/// "Balance adjustment" description <c>AdjustBalanceCommandHandler</c> writes.
/// Give it the pool category instead and the account-detail card stops counting
/// it as P&amp;L. See <see cref="MarkDescription"/>.
/// </para>
/// <para>
/// <b>Seed, CostShare and CostRecovery appear nowhere here.</b> They move no
/// money: a seed turns the owner's EXISTING balance into units (a cash leg
/// would double the account), and a cost reimbursement is a pure unit transfer
/// (a cash leg would mint value out of nothing). <c>PoolUnitEvent</c> rejects
/// cash and transaction links on all three.
/// </para>
/// </summary>
internal static class PoolMovements
{
    /// <summary>
    /// Description written on the pool's re-pricing mark. Byte-identical to
    /// <c>AdjustBalanceCommandHandler</c>'s so the two are indistinguishable
    /// downstream.
    /// </summary>
    public const string MarkDescription = "Balance adjustment";

    public static (TransactionDirection Direction, string Description) ForSubscription(string participantName) =>
        (TransactionDirection.Income, $"Pool subscription from {participantName}");

    public static (TransactionDirection Direction, string Description) ForRedemption(string participantName) =>
        (TransactionDirection.Expense, $"Pool redemption to {participantName}");

    public static (TransactionDirection Direction, string Description) ForDistribution(string participantName) =>
        (TransactionDirection.Expense, $"Pool payout to {participantName}");
}
