using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.SavingsGoals.CreateGoal;

public sealed record CreateGoalCommand(
    string Name,
    decimal TargetAmount,
    DateOnly? TargetDate,
    Guid? LinkedAccountId) : ICommand<CreateGoalResponse>;

public sealed record CreateGoalResponse(Guid Id);
