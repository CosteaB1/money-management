using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Writes the pool's re-pricing mark — the <c>IsAdjustment</c> row that brings
/// the account from what the app believes to what the exchange actually holds.
/// <para>
/// <b>Called BEFORE the NAV is struck, always.</b> That ordering is the whole
/// defence against the trap this design exists for: snapshot the pool to 3,050
/// AFTER recording a friend's 2,000 and the 2,000 books as pure trading profit,
/// in a row indistinguishable from a real one. Doing the mark first makes the
/// mistake unrepresentable instead of merely warned about — which is why no
/// handler takes "the value before" and "the value after" as two separate
/// inputs.
/// </para>
/// <para>
/// The row is deliberately shaped exactly like a hand-typed snapshot
/// (<c>IsAdjustment = true</c>, <c>IsTransfer = false</c>,
/// <see cref="SeededCategories.BalanceAdjustmentId"/>, description
/// <see cref="PoolMovements.MarkDescription"/>) so the account-detail card's
/// Net P&amp;L bucket keeps counting it. It is added to the change tracker but
/// NOT saved — the caller saves the mark, the unit events and the money legs in
/// one <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
internal static class PoolMark
{
    /// <summary>
    /// Marks <paramref name="account"/> to <paramref name="poolValueNow"/> as of
    /// <paramref name="today"/>.
    /// </summary>
    /// <param name="derivedBalance">
    /// What the app currently derives for the account (the pre-mark balance).
    /// Supplied by the caller so the balance is read once per command.
    /// </param>
    /// <returns>
    /// The delta and the transaction id, or a zero-delta outcome with no
    /// transaction. A zero delta is SKIPPED, not an error: re-typing the value
    /// the app already agrees with is the normal case at a close, and
    /// <c>Transaction.Create</c> rejects a zero amount anyway.
    /// </returns>
    public static async Task<Result<PoolMarkOutcome>> WriteAsync(
        IApplicationDbContext db,
        IFxConverter fxConverter,
        Account account,
        decimal derivedBalance,
        decimal poolValueNow,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (poolValueNow < 0m)
        {
            return Result.Failure<PoolMarkOutcome>(PoolErrors.PoolValueCannotBeNegative);
        }

        decimal delta = PoolUnitRegister.RoundMoney(poolValueNow - derivedBalance);

        if (delta == 0m)
        {
            return Result.Success(new PoolMarkOutcome(null, 0m));
        }

        string currency = account.Balance.Currency;
        var money = new Money(Math.Abs(delta), currency);

        TransactionDirection direction = delta > 0m
            ? TransactionDirection.Income
            : TransactionDirection.Expense;

        // MDL-equivalent at the movement's own date — the same contract every
        // other write path honours. Nullable propagates; downstream consumers
        // must tolerate a missing rate.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            money.Amount,
            currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        Result<Transaction> txResult = Transaction.Create(
            account.Id,
            today,
            direction,
            money,
            PoolMovements.MarkDescription,
            TransactionSource.Manual,
            categoryId: SeededCategories.BalanceAdjustmentId,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: false,
            counterAccountId: null,
            isAdjustment: true,
            amountMdl: amountMdl,
            notes: null);

        if (txResult.IsFailure)
        {
            return Result.Failure<PoolMarkOutcome>(txResult.Error);
        }

        Transaction transaction = txResult.Value;
        db.Transactions.Add(transaction);

        return Result.Success(new PoolMarkOutcome(transaction.Id, delta));
    }
}

/// <param name="TransactionId">The mark row, or <c>null</c> when the delta was zero.</param>
/// <param name="Delta">Signed change applied to the account's balance.</param>
internal sealed record PoolMarkOutcome(Guid? TransactionId, decimal Delta);
