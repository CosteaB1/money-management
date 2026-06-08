using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.SavingsGoals.GetGoals;

/// <summary>
/// Returns the active savings goals with their live progress. v1 takes no
/// parameters; archived goals are excluded.
/// </summary>
public sealed record GetGoalsQuery() : IQuery<IReadOnlyList<GoalDto>>;
