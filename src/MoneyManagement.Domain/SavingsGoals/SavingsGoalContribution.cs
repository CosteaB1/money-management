using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.SavingsGoals;

/// <summary>
/// A single delta against a manual-mode <see cref="SavingsGoal"/>'s running
/// saved amount. Written by <c>UpdateManualSavedCommandHandler</c> every time
/// the user nudges the saved value: positive amounts are contributions,
/// negative amounts are withdrawals. Zero-deltas are skipped at the caller.
/// <para>
/// Linked-mode goals do NOT use this table — their per-period contributions
/// are derived at read time from the linked account's transactions. Mixing
/// the two would double-count, since the linked balance already integrates
/// every transaction.
/// </para>
/// </summary>
public sealed class SavingsGoalContribution : Entity
{
    public const int NotesMaxLength = 500;

    // EF Core
    private SavingsGoalContribution()
    {
        Amount = Money.Zero(ReportingCurrencies.Mdl);
    }

    private SavingsGoalContribution(
        Guid id,
        Guid goalId,
        Money amount,
        DateOnly occurredOn,
        string? notes) : base(id)
    {
        GoalId = goalId;
        Amount = amount;
        OccurredOn = occurredOn;
        Notes = notes;
    }

    public Guid GoalId { get; private set; }
    public Money Amount { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public string? Notes { get; private set; }

    public static Result<SavingsGoalContribution> Create(
        Guid goalId,
        Money amount,
        DateOnly occurredOn,
        string? notes,
        IDateTimeProvider clock)
    {
        if (goalId == Guid.Empty)
        {
            return Result.Failure<SavingsGoalContribution>(SavingsGoalErrors.NotFound(goalId));
        }

        if (!string.Equals(amount.Currency, ReportingCurrencies.Mdl, StringComparison.Ordinal))
        {
            return Result.Failure<SavingsGoalContribution>(SavingsGoalErrors.MdlOnly);
        }

        if (amount.Amount == 0m)
        {
            return Result.Failure<SavingsGoalContribution>(SavingsGoalErrors.ContributionAmountMustBeNonZero);
        }

        // No future-dated contributions. Judged in UTC; the client also
        // submits/validates dates in UTC. The injected clock keeps the
        // comparison deterministic for tests.
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (occurredOn > today)
        {
            return Result.Failure<SavingsGoalContribution>(SavingsGoalErrors.ContributionOccurredOnInFuture);
        }

        string? trimmedNotes = notes?.Trim();
        if (string.IsNullOrEmpty(trimmedNotes))
        {
            trimmedNotes = null;
        }
        else if (trimmedNotes.Length > NotesMaxLength)
        {
            return Result.Failure<SavingsGoalContribution>(SavingsGoalErrors.ContributionNotesTooLong);
        }

        return new SavingsGoalContribution(Guid.CreateVersion7(), goalId, amount, occurredOn, trimmedNotes);
    }
}
