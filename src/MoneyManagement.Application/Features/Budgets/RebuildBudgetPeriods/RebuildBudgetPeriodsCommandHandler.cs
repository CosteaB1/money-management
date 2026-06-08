using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.RebuildBudgetPeriods;

/// <summary>
/// Replays every non-deleted expense for each targeted budget's category,
/// per-row FX-converted to MDL at the row's own date, and rewrites the
/// matching <see cref="BudgetPeriod"/> rows from scratch. Existing periods
/// for the targeted budgets are deleted first, so a stale period that no
/// longer has any qualifying transactions disappears.
/// </summary>
internal sealed class RebuildBudgetPeriodsCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : ICommandHandler<RebuildBudgetPeriodsCommand, RebuildBudgetPeriodsResult>
{
    public async Task<Result<RebuildBudgetPeriodsResult>> Handle(
        RebuildBudgetPeriodsCommand command,
        CancellationToken cancellationToken)
    {
        List<Budget> budgets = command.BudgetId is { } id
            ? await db.Budgets.Where(b => b.Id == id).ToListAsync(cancellationToken)
            : await db.Budgets.ToListAsync(cancellationToken);

        if (command.BudgetId is { } targetId && budgets.Count == 0)
        {
            return Result.Failure<RebuildBudgetPeriodsResult>(BudgetErrors.NotFound(targetId));
        }

        int budgetsRebuilt = 0;
        int periodsAffected = 0;

        foreach (Budget budget in budgets)
        {
            // Wipe the budget's existing periods so any drifted row disappears
            // even when the rebuild produces zero qualifying months.
            List<BudgetPeriod> existing = await db.BudgetPeriods
                .Where(p => p.BudgetId == budget.Id)
                .ToListAsync(cancellationToken);

            if (existing.Count > 0)
            {
                db.BudgetPeriods.RemoveRange(existing);
            }

            Guid categoryId = budget.CategoryId;

            List<Transaction> rows = await db.Transactions
                .Where(t =>
                    t.CategoryId == categoryId
                    && t.Direction == TransactionDirection.Expense
                    && !t.IsTransfer
                    && !t.IsAdjustment)
                .ToListAsync(cancellationToken);

            var byMonth = new Dictionary<(int Year, int Month), decimal>();

            foreach (Transaction row in rows)
            {
                decimal? amountMdl = await fxConverter.ConvertAsync(
                    row.Amount.Amount,
                    row.Amount.Currency,
                    ReportingCurrencies.Mdl,
                    row.TransactionDate,
                    cancellationToken);

                if (amountMdl is null || amountMdl.Value <= 0m)
                {
                    continue;
                }

                (int Year, int Month) key = (row.TransactionDate.Year, row.TransactionDate.Month);
                byMonth.TryGetValue(key, out decimal current);
                byMonth[key] = current + amountMdl.Value;
            }

            foreach (((int year, int month), decimal sum) in byMonth)
            {
                Result<BudgetPeriod> created = BudgetPeriod.Create(budget.Id, year, month);
                BudgetPeriodGuard.EnsureSucceeded(
                    created, $"Failed to create BudgetPeriod for budget {budget.Id}");

                BudgetPeriod period = created.Value;
                BudgetPeriodGuard.EnsureSucceeded(
                    period.AddSpend(sum), $"Failed to add spend to BudgetPeriod {period.Id}");

                db.BudgetPeriods.Add(period);
                periodsAffected++;
            }

            budgetsRebuilt++;
        }

        if (budgetsRebuilt > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new RebuildBudgetPeriodsResult(budgetsRebuilt, periodsAffected);
    }
}
