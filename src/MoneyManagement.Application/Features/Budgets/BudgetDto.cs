namespace MoneyManagement.Application.Features.Budgets;

/// <summary>
/// Read-side projection over <c>Budget</c> + the matching
/// <c>BudgetPeriod</c> (left-joined for the requested year/month). All
/// monetary fields are in MDL — budgets are MDL-only for v1.
/// </summary>
public sealed record BudgetDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    decimal MonthlyLimit,
    decimal Spent,
    decimal Remaining,
    BudgetStatus Status,
    int Year,
    int Month);
