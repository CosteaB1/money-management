using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions;

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    Guid? CategoryId,
    string? CategoryName,
    DateOnly TransactionDate,
    TransactionDirection Direction,
    decimal Amount,
    string Currency,
    decimal? AmountMdl,
    string Description,
    string? Notes,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    TransactionSource Source,
    Guid? ImportBatchId,
    bool IsTransfer,
    Guid? CounterAccountId,
    bool IsAdjustment);
