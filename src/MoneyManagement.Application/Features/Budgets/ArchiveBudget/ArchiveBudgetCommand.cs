using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Budgets.ArchiveBudget;

public sealed record ArchiveBudgetCommand(Guid Id) : ICommand;
