using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Builds the transaction row that carries a pool movement's real money.
/// Pure — the caller does the FX lookup, adds the row and saves, so every leg
/// of a command lands in one <c>SaveChangesAsync</c> with its unit events.
/// <para>
/// The shape is fixed here rather than at each call site so no handler can
/// forget <c>IsTransfer</c> (which is what keeps pool cash out of income/expense
/// aggregates) or route the row to the wrong category.
/// </para>
/// </summary>
internal static class PoolMoneyLeg
{
    /// <param name="accountId">The account the money moves on.</param>
    /// <param name="date">The date the money moved — the leg's own date, which is also the FX date.</param>
    /// <param name="direction">From <see cref="PoolMovements"/>.</param>
    /// <param name="money">Amount and currency; always the pool's currency.</param>
    /// <param name="description">From <see cref="PoolMovements"/>.</param>
    /// <param name="counterAccountId">
    /// The other side when the money lands in a tracked account (an owner
    /// withdrawal to Bybit). <c>null</c> when the counterparty is outside the
    /// app — a friend's subscription arriving, or a payout leaving.
    /// </param>
    /// <param name="amountMdl">Reporting-currency value at <paramref name="date"/>; may be null when no rate exists.</param>
    /// <param name="notes">Free text carried from the unit event.</param>
    public static Result<Transaction> Create(
        Guid accountId,
        DateOnly date,
        TransactionDirection direction,
        Money money,
        string description,
        Guid? counterAccountId,
        decimal? amountMdl,
        string? notes) =>
        Transaction.Create(
            accountId,
            date,
            direction,
            money,
            description,
            TransactionSource.Manual,
            categoryId: SeededCategories.PoolId,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            // Transfer-flagged so the movement stays out of every P&L aggregate
            // — the Investment/Withdrawal and Loan precedent.
            isTransfer: true,
            counterAccountId: counterAccountId,
            isAdjustment: false,
            amountMdl: amountMdl,
            notes: notes);
}
