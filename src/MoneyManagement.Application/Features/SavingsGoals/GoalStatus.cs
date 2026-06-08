namespace MoneyManagement.Application.Features.SavingsGoals;

/// <summary>
/// Pace bucket for a single <c>GoalDto</c>. Order of evaluation in the read
/// handler:
/// <list type="number">
///   <item>
///     <description><see cref="Achieved"/> — saved ≥ target, regardless of date.</description>
///   </item>
///   <item>
///     <description><see cref="OnTrack"/> — no target date set, OR before the date and at/above pace.</description>
///   </item>
///   <item>
///     <description><see cref="AtRisk"/> — before the date but behind pace.</description>
///   </item>
///   <item>
///     <description><see cref="Behind"/> — past the target date and not achieved.</description>
///   </item>
/// </list>
/// </summary>
public enum GoalStatus
{
    OnTrack = 0,
    AtRisk = 1,
    Achieved = 2,
    Behind = 3,
}
