using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.SavingsGoals.ArchiveGoal;

public sealed record ArchiveGoalCommand(Guid Id) : ICommand;
