namespace MoneyManagement.Domain.Categories;

/// <summary>
/// Deterministic ids for categories inserted by the infrastructure-level
/// <c>CategorySeeder</c>. Surfaced in the domain so application handlers can
/// reference them without taking an infrastructure dependency.
/// </summary>
public static class SeededCategories
{
    /// <summary>
    /// "Balance Adjustment" - applied to month-end adjustment transactions
    /// (Phase 4). Flow is <see cref="CategoryFlow.Both"/> since adjustments
    /// can be positive (income) or negative (expense).
    /// </summary>
    public static readonly Guid BalanceAdjustmentId = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>
    /// "Bank Fees" - applied to <c>Comision: …</c> rows emitted by the maib
    /// PDF parser when its <c>ieșiri</c> column carries a paired
    /// <c>comision</c>. The parser splits the bank's combined debit into a
    /// principal expense and a fee expense whose sum equals the original
    /// <c>ieșiri</c>, so fees count toward the live balance like any other
    /// expense. This id is used by the category suggester (and surfacing in
    /// category breakdowns); balance handlers do NOT special-case it.
    /// </summary>
    public static readonly Guid BankFeesId = new("00000000-0000-0000-0000-00000000000d");

    /// <summary>
    /// "Investment" - applied to money moved INTO an investment-style account
    /// (Brokerage / CryptoExchange / P2PLending / BankDeposit) via the
    /// "Investment" balance-change mode. Recorded as a transfer-flagged income
    /// row (not a P&amp;L adjustment). Flow is <see cref="CategoryFlow.Both"/>.
    /// </summary>
    public static readonly Guid InvestmentId = new("00000000-0000-0000-0000-00000000000e");

    /// <summary>
    /// "Withdrawal" - applied to money moved OUT of an investment-style account
    /// via the "Withdrawal" balance-change mode. Recorded as a transfer-flagged
    /// expense row (not a P&amp;L adjustment). Flow is <see cref="CategoryFlow.Both"/>.
    /// </summary>
    public static readonly Guid WithdrawalId = new("00000000-0000-0000-0000-00000000000f");

    /// <summary>
    /// "Loan" - applied to the transactions synthesized by the Loans slice
    /// (disbursements and repayments recorded against an account). Those rows
    /// are transfer-flagged so they stay out of income/expense aggregates —
    /// same precedent as Investment/Withdrawal. Flow is
    /// <see cref="CategoryFlow.Both"/> since a loan movement can be either
    /// direction depending on who lent to whom.
    /// </summary>
    public static readonly Guid LoanId = new("00000000-0000-0000-0000-000000000012");

    /// <summary>
    /// "Pool" - applied to the transactions synthesized by the Pools slice
    /// (subscriptions, redemptions and distribution payouts recorded against the
    /// pooled account). Those rows are transfer-flagged so they stay out of
    /// income/expense aggregates - same precedent as Investment/Withdrawal and
    /// Loan. Flow is <see cref="CategoryFlow.Both"/> because capital moves both
    /// ways (money in from an investor, money out as a payout or exit).
    /// <para>
    /// The pool's re-pricing MARK is deliberately NOT categorised here: it is a
    /// real <c>IsAdjustment</c> row and keeps
    /// <see cref="BalanceAdjustmentId"/>, so the account-detail "Net P&amp;L"
    /// bucket keeps counting it exactly as a hand-typed snapshot.
    /// </para>
    /// </summary>
    public static readonly Guid PoolId = new("00000000-0000-0000-0000-000000000013");
}
