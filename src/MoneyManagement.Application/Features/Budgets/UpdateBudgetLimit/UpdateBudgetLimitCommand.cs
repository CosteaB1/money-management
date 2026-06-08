using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Budgets.UpdateBudgetLimit;

public sealed record UpdateBudgetLimitCommand(Guid Id, decimal MonthlyLimit) : ICommand;
