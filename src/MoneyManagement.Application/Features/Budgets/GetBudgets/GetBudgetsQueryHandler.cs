using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.GetBudgets;

internal sealed class GetBudgetsQueryHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : IQueryHandler<GetBudgetsQuery, IReadOnlyList<BudgetDto>>
{
    // Thresholds match the dashboard color story (green / yellow / red).
    private const decimal WarningThreshold = 0.80m;
    private const decimal OverThreshold = 1.00m;

    public async Task<Result<IReadOnlyList<BudgetDto>>> Handle(
        GetBudgetsQuery query,
        CancellationToken cancellationToken)
    {
        DateTime now = clock.UtcNow;
        int year = query.Year ?? now.Year;
        int month = query.Month ?? now.Month;

        // The `is_archived = false` global query filter excludes archived
        // budgets under EF Core. The explicit `!b.IsArchived` predicate is
        // defense-in-depth so unit tests (which bypass model configuration)
        // exercise the same predicate the production query relies on - mirrors
        // GetSummaryQueryHandler. Join via subquery so the absence of a
        // BudgetPeriod row materializes as Spent = 0 in the projection.
        var rows = await db.Budgets
            .Where(b => !b.IsArchived)
            .Select(b => new
            {
                b.Id,
                b.CategoryId,
                CategoryName = db.Categories
                    .Where(c => c.Id == b.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                MonthlyLimit = b.MonthlyLimit.Amount,
                Spent = db.BudgetPeriods
                    .Where(p => p.BudgetId == b.Id && p.Year == year && p.Month == month)
                    .Select(p => (decimal?)p.Spent.Amount)
                    .FirstOrDefault() ?? 0m,
            })
            .ToListAsync(cancellationToken);

        var result = rows
            .Select(r => new BudgetDto(
                r.Id,
                r.CategoryId,
                r.CategoryName ?? string.Empty,
                r.MonthlyLimit,
                r.Spent,
                r.MonthlyLimit - r.Spent,
                ComputeStatus(r.Spent, r.MonthlyLimit),
                year,
                month))
            .ToList();

        return Result.Success<IReadOnlyList<BudgetDto>>(result);
    }

    private static BudgetStatus ComputeStatus(decimal spent, decimal limit)
    {
        if (limit <= 0m)
        {
            // Defensive: factory guarantees limit > 0, but a future migration
            // shouldn't silently divide by zero if it loosens the rule.
            return BudgetStatus.OnTrack;
        }

        decimal ratio = spent / limit;

        if (ratio > OverThreshold)
        {
            return BudgetStatus.Over;
        }

        if (ratio >= WarningThreshold)
        {
            return BudgetStatus.Warning;
        }

        return BudgetStatus.OnTrack;
    }
}
