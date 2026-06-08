namespace MoneyManagement.Application.Features.Budgets;

/// <summary>
/// Health bucket for a single <c>BudgetDto</c>. Thresholds are pinned by the
/// dashboard color story (green / yellow / red). The cutoffs match the
/// product brief in <c>WIKI.md</c>: <c>OnTrack</c> below 80%, <c>Warning</c>
/// in [80%, 100%], <c>Over</c> above 100%.
/// </summary>
public enum BudgetStatus
{
    OnTrack = 0,
    Warning = 1,
    Over = 2,
}
