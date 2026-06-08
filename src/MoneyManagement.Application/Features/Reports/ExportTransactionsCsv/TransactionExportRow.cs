using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Reports.ExportTransactionsCsv;

/// <summary>
/// Flat row shape consumed by the CSV serializer in the API layer. Joins the
/// account and category names up-front so the endpoint doesn't N+1 the lookup
/// while writing rows to the response body.
/// </summary>
public sealed record TransactionExportRow(
    DateOnly TransactionDate,
    string AccountName,
    string CategoryName,
    TransactionDirection Direction,
    decimal Amount,
    string Currency,
    decimal? AmountMdl,
    string Description,
    bool IsTransfer,
    bool IsAdjustment);
