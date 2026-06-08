using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Transactions.DeleteTransaction;

public sealed record DeleteTransactionCommand(Guid Id) : ICommand;
