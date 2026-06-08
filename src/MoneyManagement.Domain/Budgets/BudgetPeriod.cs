using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Budgets;

/// <summary>
/// Running spend for a single <see cref="Budget"/> in a single calendar month.
/// Created on demand by the domain-event handler the first time an expense in
/// that month hits the budget's category, then incremented per transaction.
/// </summary>
public sealed class BudgetPeriod : Entity
{
    // EF Core
    private BudgetPeriod()
    {
        Spent = Money.Zero(ReportingCurrencies.Mdl);
    }

    private BudgetPeriod(
        Guid id,
        Guid budgetId,
        int year,
        int month) : base(id)
    {
        BudgetId = budgetId;
        Year = year;
        Month = month;
        Spent = Money.Zero(ReportingCurrencies.Mdl);
    }

    public Guid BudgetId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public Money Spent { get; private set; }

    public static Result<BudgetPeriod> Create(Guid budgetId, int year, int month)
    {
        if (budgetId == Guid.Empty)
        {
            return Result.Failure<BudgetPeriod>(BudgetPeriodErrors.BudgetRequired);
        }

        if (year <= 0)
        {
            return Result.Failure<BudgetPeriod>(BudgetPeriodErrors.InvalidYear);
        }

        if (month is < 1 or > 12)
        {
            return Result.Failure<BudgetPeriod>(BudgetPeriodErrors.InvalidMonth);
        }

        return new BudgetPeriod(Guid.CreateVersion7(), budgetId, year, month);
    }

    public Result AddSpend(decimal amountMdl)
    {
        if (amountMdl <= 0m)
        {
            return Result.Failure(BudgetPeriodErrors.SpendMustBePositive);
        }

        Spent = new Money(Spent.Amount + amountMdl, ReportingCurrencies.Mdl);
        return Result.Success();
    }

    /// <summary>
    /// Inverse of <see cref="AddSpend"/>, used by the
    /// <c>TransactionDeleted</c> / <c>TransactionCategoryChanged</c> handlers.
    /// Clamps at zero so FX-rate drift between create-time and delete-time
    /// (the converter resolves the most recent rate ≤ asOf, and that rate can
    /// shift in either direction) never produces a nonsensical negative
    /// rollup. The canonical correction path for any accumulated drift is
    /// <c>RebuildBudgetPeriods</c>.
    /// </summary>
    public Result SubtractSpend(decimal amountMdl)
    {
        if (amountMdl <= 0m)
        {
            return Result.Failure(BudgetPeriodErrors.SpendMustBePositive);
        }

        decimal next = Spent.Amount - amountMdl;
        Spent = next < 0m
            ? Money.Zero(ReportingCurrencies.Mdl)
            : new Money(next, ReportingCurrencies.Mdl);
        return Result.Success();
    }
}
