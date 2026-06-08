using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Budgets;

/// <summary>
/// Per-category monthly spending limit. v1 is MDL-only (the app's reporting
/// currency); multi-currency budgets can come later if a real need surfaces.
/// </summary>
public sealed class Budget : Entity
{
    // EF Core
    private Budget()
    {
        MonthlyLimit = Money.Zero(ReportingCurrencies.Mdl);
    }

    private Budget(
        Guid id,
        Guid categoryId,
        Money monthlyLimit) : base(id)
    {
        CategoryId = categoryId;
        MonthlyLimit = monthlyLimit;
        IsArchived = false;
    }

    public Guid CategoryId { get; private set; }
    public Money MonthlyLimit { get; private set; }
    public bool IsArchived { get; private set; }

    public static Result<Budget> Create(Guid categoryId, Money monthlyLimit)
    {
        if (categoryId == Guid.Empty)
        {
            return Result.Failure<Budget>(BudgetErrors.NotFound(categoryId));
        }

        Result validation = ValidateLimit(monthlyLimit);
        if (validation.IsFailure)
        {
            return Result.Failure<Budget>(validation.Error);
        }

        return new Budget(Guid.CreateVersion7(), categoryId, monthlyLimit);
    }

    public Result UpdateLimit(Money newLimit)
    {
        Result validation = ValidateLimit(newLimit);
        if (validation.IsFailure)
        {
            return validation;
        }

        MonthlyLimit = newLimit;
        return Result.Success();
    }

    /// <summary>Idempotent: archiving an already-archived budget is a no-op.</summary>
    public Result Archive()
    {
        IsArchived = true;
        return Result.Success();
    }

    private static Result ValidateLimit(Money limit)
    {
        if (limit.Amount <= 0m)
        {
            return Result.Failure(BudgetErrors.LimitMustBePositive);
        }

        if (!string.Equals(limit.Currency, ReportingCurrencies.Mdl, StringComparison.Ordinal))
        {
            return Result.Failure(BudgetErrors.MdlOnly);
        }

        return Result.Success();
    }
}
