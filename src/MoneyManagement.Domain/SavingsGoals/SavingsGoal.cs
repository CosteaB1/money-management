using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.SavingsGoals;

/// <summary>
/// User-defined savings target. Supports two mutually exclusive modes:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Linked-account mode</b> — <see cref="LinkedAccountId"/> is set;
///       <see cref="ManualSavedAmount"/> is <c>null</c>. The read-side handler
///       computes the saved amount live from the linked account's MDL balance.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Manual mode</b> — <see cref="LinkedAccountId"/> is <c>null</c>;
///       <see cref="ManualSavedAmount"/> holds a writable MDL-denominated
///       value, defaulted to zero at creation.
///     </description>
///   </item>
/// </list>
/// v1 is MDL-only — mirrors the Budget slice's reporting-currency rule.
/// </summary>
public sealed class SavingsGoal : Entity
{
    public const int NameMaxLength = 100;

    // EF Core
    private SavingsGoal()
    {
        Name = string.Empty;
        TargetAmount = Money.Zero(ReportingCurrencies.Mdl);
    }

    private SavingsGoal(
        Guid id,
        string name,
        Money targetAmount,
        DateOnly? targetDate,
        Guid? linkedAccountId,
        Money? manualSavedAmount) : base(id)
    {
        Name = name;
        TargetAmount = targetAmount;
        TargetDate = targetDate;
        LinkedAccountId = linkedAccountId;
        SetManualSavedInternal(manualSavedAmount);
        IsArchived = false;
    }

    public string Name { get; private set; }
    public Money TargetAmount { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public Guid? LinkedAccountId { get; private set; }

    // Scalar columns backing the nullable ManualSavedAmount Money?. EF Core 10
    // does not map a nullable ComplexProperty cleanly, so we persist the two
    // primitives directly. They are paired - both NULL in linked mode, both
    // populated in manual mode. The public ManualSavedAmount getter
    // reconstructs the value object from this pair.
    private decimal? ManualSavedAmountValue { get; set; }

    private string? ManualSavedAmountCurrency { get; set; }

    /// <summary>
    /// Manual-mode saved amount. <c>null</c> when the goal is linked to an
    /// account (the read side computes the saved amount from the account's
    /// balance in that case). Always MDL when set.
    /// </summary>
    public Money? ManualSavedAmount =>
        ManualSavedAmountValue is decimal value && ManualSavedAmountCurrency is string currency
            ? new Money(value, currency)
            : null;

    public bool IsArchived { get; private set; }

    public static Result<SavingsGoal> Create(
        string name,
        Money targetAmount,
        DateOnly? targetDate,
        Guid? linkedAccountId,
        IDateTimeProvider clock)
    {
        Result<string> nameValidation = ValidateName(name);
        if (nameValidation.IsFailure)
        {
            return Result.Failure<SavingsGoal>(nameValidation.Error);
        }

        Result targetValidation = ValidateTarget(targetAmount);
        if (targetValidation.IsFailure)
        {
            return Result.Failure<SavingsGoal>(targetValidation.Error);
        }

        Result targetDateValidation = ValidateTargetDate(targetDate, clock);
        if (targetDateValidation.IsFailure)
        {
            return Result.Failure<SavingsGoal>(targetDateValidation.Error);
        }

        // Manual mode is implied by the absence of a linked account at
        // creation; start its running saved amount at zero. Linked mode keeps
        // ManualSavedAmount null - the read side ignores it and pulls the
        // live MDL balance off the linked account instead.
        Money? manualSaved = linkedAccountId is null
            ? Money.Zero(ReportingCurrencies.Mdl)
            : null;

        return new SavingsGoal(
            Guid.CreateVersion7(),
            nameValidation.Value,
            targetAmount,
            targetDate,
            linkedAccountId,
            manualSaved);
    }

    public Result Rename(string newName)
    {
        Result<string> validation = ValidateName(newName);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        Name = validation.Value;
        return Result.Success();
    }

    public Result UpdateTarget(Money newTarget)
    {
        Result validation = ValidateTarget(newTarget);
        if (validation.IsFailure)
        {
            return validation;
        }

        TargetAmount = newTarget;
        return Result.Success();
    }

    public Result UpdateTargetDate(DateOnly? newDate, IDateTimeProvider clock)
    {
        Result validation = ValidateTargetDate(newDate, clock);
        if (validation.IsFailure)
        {
            return validation;
        }

        TargetDate = newDate;
        return Result.Success();
    }

    /// <summary>
    /// Switches the goal into linked-account mode. The user is free to
    /// re-link to a different account at any time; the manual-saved value
    /// (if any) is dropped because the linked account becomes the source of
    /// truth. Idempotent when already pointed at the same account.
    /// </summary>
    public Result LinkAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            return Result.Failure(SavingsGoalErrors.NotFound(accountId));
        }

        LinkedAccountId = accountId;
        SetManualSavedInternal(null);
        return Result.Success();
    }

    /// <summary>
    /// Switches the goal from linked into manual mode, resetting the
    /// manual-saved value to zero so the user starts tracking from a clean
    /// slate. Idempotent and non-destructive when the goal is ALREADY in manual
    /// mode: the saved amount is preserved (defense-in-depth so an accidental
    /// re-unlink can't wipe the user's progress).
    /// </summary>
    public Result Unlink()
    {
        // Only a real linked -> manual transition resets to zero. If the goal is
        // already unlinked, leave the existing manual-saved amount intact.
        if (LinkedAccountId is not null)
        {
            LinkedAccountId = null;
            SetManualSavedInternal(Money.Zero(ReportingCurrencies.Mdl));
        }

        return Result.Success();
    }

    public Result SetManualSaved(Money newAmount)
    {
        if (LinkedAccountId is not null)
        {
            return Result.Failure(SavingsGoalErrors.NotInManualMode);
        }

        if (!string.Equals(newAmount.Currency, ReportingCurrencies.Mdl, StringComparison.Ordinal))
        {
            return Result.Failure(SavingsGoalErrors.MdlOnly);
        }

        if (newAmount.Amount < 0m)
        {
            return Result.Failure(SavingsGoalErrors.ManualSavedMustBeNonNegative);
        }

        SetManualSavedInternal(newAmount);
        return Result.Success();
    }

    /// <summary>Idempotent: archiving an already-archived goal is a no-op.</summary>
    public Result Archive()
    {
        IsArchived = true;
        return Result.Success();
    }

    private void SetManualSavedInternal(Money? value)
    {
        if (value is Money money)
        {
            ManualSavedAmountValue = money.Amount;
            ManualSavedAmountCurrency = money.Currency;
        }
        else
        {
            ManualSavedAmountValue = null;
            ManualSavedAmountCurrency = null;
        }
    }

    private static Result<string> ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<string>(SavingsGoalErrors.NameRequired);
        }

        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            return Result.Failure<string>(SavingsGoalErrors.NameTooLong);
        }

        return Result.Success(trimmed);
    }

    private static Result ValidateTarget(Money target)
    {
        if (target.Amount <= 0m)
        {
            return Result.Failure(SavingsGoalErrors.TargetMustBePositive);
        }

        if (!string.Equals(target.Currency, ReportingCurrencies.Mdl, StringComparison.Ordinal))
        {
            return Result.Failure(SavingsGoalErrors.MdlOnly);
        }

        return Result.Success();
    }

    private static Result ValidateTargetDate(DateOnly? targetDate, IDateTimeProvider clock)
    {
        if (targetDate is null)
        {
            return Result.Success();
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (targetDate.Value < today)
        {
            return Result.Failure(SavingsGoalErrors.TargetDateInPast);
        }

        return Result.Success();
    }
}
