using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Transactions.UpdateTransactionCategory;

public sealed record UpdateTransactionCategoryCommand(Guid Id, Guid? CategoryId) : ICommand;
