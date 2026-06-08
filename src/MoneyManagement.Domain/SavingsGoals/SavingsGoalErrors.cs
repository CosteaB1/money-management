using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.SavingsGoals;

public static class SavingsGoalErrors
{
    public static readonly Error NameRequired =
        Error.Validation("savings_goal.name_required", "Savings goal name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("savings_goal.name_too_long", "Savings goal name must be 100 characters or fewer.");

    public static readonly Error TargetMustBePositive =
        Error.Validation("savings_goal.target_must_be_positive", "Target amount must be greater than zero.");

    public static readonly Error MdlOnly =
        Error.Validation(
            "savings_goal.mdl_only",
            "Savings goals are denominated in MDL only for v1.");

    public static readonly Error TargetDateInPast =
        Error.Validation("savings_goal.target_date_in_past", "Target date cannot be in the past.");

    public static readonly Error ManualSavedMustBeNonNegative =
        Error.Validation(
            "savings_goal.manual_saved_must_be_non_negative",
            "Manual saved amount cannot be negative.");

    public static readonly Error NotInManualMode =
        Error.Validation(
            "savings_goal.not_in_manual_mode",
            "Cannot set manual saved amount: this goal is linked to an account.");

    public static readonly Error ContributionAmountMustBeNonZero =
        Error.Validation(
            "savings_goal.contribution_amount_must_be_non_zero",
            "Contribution amount must be non-zero (positive for deposits, negative for withdrawals).");

    public static readonly Error ContributionNotesTooLong =
        Error.Validation(
            "savings_goal.contribution_notes_too_long",
            "Contribution notes must be 500 characters or fewer.");

    public static readonly Error ContributionOccurredOnInFuture =
        Error.Validation(
            "savings_goal.contribution_occurred_on_in_future",
            "Contribution date cannot be in the future.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("savings_goal.not_found", $"Savings goal with id '{id}' was not found.");
}
