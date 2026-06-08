using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Transactions.CreateTransfer;

public sealed record CreateTransferCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    DateOnly Date,
    string Description,
    Guid? CategoryId,
    decimal? DestinationAmount = null,
    string? Notes = null) : ICommand<TransferResult>;

public sealed record TransferResult(Guid SourceTransactionId, Guid DestinationTransactionId);
