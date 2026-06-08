using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.SavingsGoals.GetGoalDetail;

public sealed record GetGoalDetailQuery(Guid Id) : IQuery<GoalDetailDto>;
