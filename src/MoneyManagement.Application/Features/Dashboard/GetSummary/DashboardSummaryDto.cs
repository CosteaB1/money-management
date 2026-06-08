namespace MoneyManagement.Application.Features.Dashboard.GetSummary;

/// <summary>
/// Aggregate of "real" income/expense over a single calendar month, both sides
/// FX-converted to MDL at each transaction's own date.
/// </summary>
/// <param name="Month">
/// The window identifier in <c>YYYY-MM</c> format. Matches what the caller
/// passed in (or the current UTC month when omitted).
/// </param>
/// <param name="Income">Σ AmountMdl of non-transfer, non-adjustment income transactions in window.</param>
/// <param name="Expense">Σ AmountMdl of non-transfer, non-adjustment expense transactions in window.</param>
/// <param name="Net">Income − Expense.</param>
/// <param name="SavingsRate">Net / Income; 0 when income is 0.</param>
/// <param name="TransactionCount">Count of rows that contributed to the totals (i.e. after filters and after FX-convertible filter).</param>
/// <param name="MissingFxRate">
/// True when at least one filtered row was omitted from the totals because its
/// amount could not be FX-converted to MDL at its transaction date.
/// </param>
public sealed record DashboardSummaryDto(
    string Month,
    decimal Income,
    decimal Expense,
    decimal Net,
    decimal SavingsRate,
    int TransactionCount,
    bool MissingFxRate);
