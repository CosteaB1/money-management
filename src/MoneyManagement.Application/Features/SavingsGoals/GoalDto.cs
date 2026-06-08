namespace MoneyManagement.Application.Features.SavingsGoals;

/// <summary>
/// Read-side projection over a <c>SavingsGoal</c>. All monetary fields are in
/// MDL (the goal's native currency in v1). <see cref="Saved"/> is computed
/// live from the linked account's balance when
/// <see cref="LinkedAccountId"/> is set, or read from the goal's
/// <c>ManualSavedAmount</c> in manual mode.
/// </summary>
public sealed record GoalDto(
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
    bool MissingFxRate);
