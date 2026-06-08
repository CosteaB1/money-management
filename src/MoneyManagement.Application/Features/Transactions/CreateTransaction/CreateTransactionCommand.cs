using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions.CreateTransaction;

public sealed record CreateTransactionCommand(
    Guid AccountId,
    DateOnly TransactionDate,
    TransactionDirection Direction,
    decimal Amount,
    string Description,
    Guid? CategoryId,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    bool IsTransfer = false,
    Guid? CounterAccountId = null,
    bool IsAdjustment = false,
    string? Currency = null,
    string? Notes = null) : ICommand<Guid>;
