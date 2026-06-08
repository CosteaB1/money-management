using MoneyManagement.Domain.SavingsGoals;

namespace MoneyManagement.Application.Features.SavingsGoals;

/// <summary>
/// Shared read-side math for projecting a <see cref="SavingsGoal"/> onto its
/// status/remaining/required-monthly fields. Lives here so both the list and
/// detail handlers stay in lock-step — every rule change goes through one
/// helper instead of drifting between two slightly-different copies.
/// </summary>
internal static class GoalProjection
{
    // Bucket cutoff for AtRisk vs OnTrack — being within 10% of pace is
    // "close enough". Tight enough to flag a real shortfall, loose enough
    // not to nag on rounding noise.
    private const decimal OnTrackPaceTolerance = 0.90m;

    public readonly record struct Projection(
        decimal Remaining,
        decimal ProgressPercent,
        GoalStatus Status,
        decimal? RequiredMonthlyContribution);

    public static Projection Project(SavingsGoal goal, decimal saved, DateOnly today)
    {
        decimal target = goal.TargetAmount.Amount;
        decimal remaining = Math.Max(0m, target - saved);
        decimal progress = target > 0m ? saved / target : 0m;
        GoalStatus status = ComputeStatus(saved, target, goal.TargetDate, goal.CreatedAt, today);
        decimal? required = ComputeRequiredMonthlyContribution(saved, target, goal.TargetDate, today);
        return new Projection(remaining, progress, status, required);
    }

    /// <summary>
    /// Approximate calendar-months between two dates as a decimal. 30.4375 is
    /// the long-run average days-per-month (365.25 / 12). Plenty good enough
    /// for the pace heuristic — we're not pricing a bond.
    /// </summary>
    public static decimal MonthsBetween(DateOnly from, DateOnly to)
    {
        if (to <= from)
        {
            return 0m;
        }

        int days = to.DayNumber - from.DayNumber;
        return days / 30.4375m;
    }

    private static GoalStatus ComputeStatus(
        decimal saved,
        decimal target,
        DateOnly? targetDate,
        DateTime createdAt,
        DateOnly today)
    {
        if (saved >= target)
        {
            return GoalStatus.Achieved;
        }

        if (targetDate is null)
        {
            return GoalStatus.OnTrack;
        }

        if (today > targetDate.Value)
        {
            return GoalStatus.Behind;
        }

        var createdDate = DateOnly.FromDateTime(createdAt);
        decimal monthsElapsed = MonthsBetween(createdDate, today);
        decimal monthsTotal = Math.Max(1m, MonthsBetween(createdDate, targetDate.Value));

        decimal expectedSaved = target * (monthsElapsed / monthsTotal);
        if (saved >= expectedSaved * OnTrackPaceTolerance)
        {
            return GoalStatus.OnTrack;
        }

        return GoalStatus.AtRisk;
    }

    private static decimal? ComputeRequiredMonthlyContribution(
        decimal saved,
        decimal target,
        DateOnly? targetDate,
        DateOnly today)
    {
        if (targetDate is null)
        {
            return null;
        }

        if (saved >= target)
        {
            return null;
        }

        decimal monthsRemaining = MonthsBetween(today, targetDate.Value);
        decimal effectiveMonths = Math.Max(1m, Math.Ceiling(monthsRemaining));
        return (target - saved) / effectiveMonths;
    }
}
