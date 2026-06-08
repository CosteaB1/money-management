namespace MoneyManagement.Application.Features.Reports.GetMonthlySummary;

/// <summary>
/// One calendar month's income/expense aggregate, FX-converted to MDL at each
/// transaction's own date. Shape mirrors
/// <see cref="MoneyManagement.Application.Features.Dashboard.GetSummary.DashboardSummaryDto"/>.
/// </summary>
public sealed record MonthlySummaryPointDto(
    string Month,
    decimal Income,
    decimal Expense,
    decimal Net,
    decimal SavingsRate,
    int TransactionCount,
    bool MissingFxRate);
