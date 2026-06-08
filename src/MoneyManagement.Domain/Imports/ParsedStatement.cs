using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Domain.Imports;

public sealed record ParsedStatement(
    ParsedStatementPeriod Period,
    ParsedStatementSummary Summary,
    IReadOnlyList<ParsedStatementRow> Rows);

public sealed record ParsedStatementPeriod(DateOnly From, DateOnly To);

public sealed record ParsedStatementSummary(
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal TotalFees);

public sealed record ParsedStatementRow(
    DateOnly TransactionDate,
    TransactionDirection Direction,
    decimal AmountMdl,
    string Description,
    decimal? OriginalAmount,
    string? OriginalCurrency);
