using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Budgets;

public static class BudgetErrors
{
    public static readonly Error LimitMustBePositive =
        Error.Validation("budget.limit_must_be_positive", "Monthly limit must be greater than zero.");

    public static readonly Error MdlOnly =
        Error.Validation(
            "budget.mdl_only",
            "Budgets are denominated in MDL only for v1.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("budget.not_found", $"Budget with id '{id}' was not found.");

    public static Error AlreadyExistsForCategory(Guid categoryId) =>
        Error.Conflict(
            "budget.already_exists_for_category",
            $"An active budget already exists for category '{categoryId}'.");
}
