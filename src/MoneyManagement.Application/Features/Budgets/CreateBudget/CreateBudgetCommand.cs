using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Budgets.CreateBudget;

public sealed record CreateBudgetCommand(Guid CategoryId, decimal MonthlyLimit)
    : ICommand<CreateBudgetResponse>;

public sealed record CreateBudgetResponse(Guid Id);
