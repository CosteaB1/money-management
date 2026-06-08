using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.SavingsGoals.UpdateGoal;

public sealed record UpdateGoalCommand(
    Guid Id,
    string Name,
    decimal TargetAmount,
    DateOnly? TargetDate,
    Guid? LinkedAccountId) : ICommand;
