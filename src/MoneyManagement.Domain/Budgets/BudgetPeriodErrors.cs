using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Budgets;

public static class BudgetPeriodErrors
{
    public static readonly Error SpendMustBePositive =
        Error.Validation("budget_period.spend_must_be_positive", "Spend amount must be greater than zero.");

    public static readonly Error BudgetRequired =
        Error.Validation("budget_period.budget_required", "Budget id is required.");

    public static readonly Error InvalidMonth =
        Error.Validation("budget_period.invalid_month", "Month must be in the range 1..12.");

    public static readonly Error InvalidYear =
        Error.Validation("budget_period.invalid_year", "Year must be a positive value.");
}
