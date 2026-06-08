using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Reports.ExportTransactionsCsv;

public sealed record ExportTransactionsCsvQuery(
    Guid? AccountId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? CategoryId = null,
    TransactionDirection? Direction = null,
    bool? IsTransfer = null,
    bool? IsAdjustment = null) : IQuery<IReadOnlyList<TransactionExportRow>>;
