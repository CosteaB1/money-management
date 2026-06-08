namespace MoneyManagement.Application.Features.SavingsGoals;

/// <summary>
/// Drill-down projection for a single <see cref="MoneyManagement.Domain.SavingsGoals.SavingsGoal"/>.
/// Carries the same surface as <see cref="GoalDto"/> plus the contribution
/// history, a per-month saved-over-time series, and pace stats used by the
/// frontend's goal-detail page.
/// </summary>
public sealed record GoalDetailDto(
    Guid Id,
    string Name,
    decimal TargetAmount,
    DateOnly? TargetDate,
    Guid? LinkedAccountId,
    string? LinkedAccountName,
    decimal Saved,
    decimal Remaining,
    decimal ProgressPercent,
    GoalStatus Status,
    decimal? RequiredMonthlyContribution,
    bool IsLinkedMode,
    bool MissingFxRate,
    DateOnly CreatedOn,
    bool IsArchived,
    GoalPaceStatsDto Pace,
    IReadOnlyList<GoalContributionDto> Contributions,
    IReadOnlyList<GoalSavedPointDto> SavedHistory);

/// <summary>
/// One row in a goal's contribution history. <see cref="Id"/> is null when the
/// row is derived from a linked account's transaction at read time (linked-
/// mode goals don't persist their own contribution rows).
/// </summary>
public sealed record GoalContributionDto(
    Guid? Id,
    decimal Amount,
    DateOnly OccurredOn,
    string? Notes,
    GoalContributionSource Source);

public enum GoalContributionSource
{
    Manual = 0,
    LinkedAccountTransaction = 1,
}

/// <summary>
/// Single point on the saved-over-time series. <see cref="Saved"/> is the
/// running cumulative MDL value at <see cref="AsOf"/> end-of-day.
/// </summary>
public sealed record GoalSavedPointDto(
    DateOnly AsOf,
    decimal Saved);

/// <summary>
/// Pace stats derived from the trailing 90 days of contribution activity. Any
/// field may be null when there isn't enough history (or pace is non-positive)
/// — the frontend treats null as "not yet projectable".
/// </summary>
public sealed record GoalPaceStatsDto(
    decimal? AvgMonthlyContribution,
    DateOnly? ProjectedCompletionDate,
    decimal? MonthsToAchieveAtPace);
