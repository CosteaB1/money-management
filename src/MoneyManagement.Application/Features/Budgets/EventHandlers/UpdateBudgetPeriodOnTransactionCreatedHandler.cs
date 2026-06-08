using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.EventHandlers;

/// <summary>
/// Updates the running spend for a category's active budget whenever a real
/// expense lands. This is the project's first
/// <see cref="IDomainEventHandler{TEvent}"/>; the dispatcher runs it after the
/// emitting transaction's <c>SaveChanges</c> completes (see
/// <c>DomainEventsDispatcher</c>), so re-saving here does not retrigger the
/// same event - we only insert or mutate <c>BudgetPeriod</c> rows.
/// </summary>
/// <remarks>
/// <para>Skip rules — short-circuit before doing any DB work:</para>
/// <list type="bullet">
///   <item>Uncategorized transactions (no <c>CategoryId</c>).</item>
///   <item>Transfers and balance adjustments (not real P&amp;L).</item>
///   <item>Income rows (budgets cap spending, not income).</item>
///   <item>Rows whose MDL value couldn't be resolved (no usable FX rate).</item>
/// </list>
/// <para>If no active budget exists for the category, the handler returns
/// silently — most categories don't have a budget configured.</para>
/// <para>v1 limitation: there is no companion handler for
/// <c>TransactionDeleted</c> or <c>TransactionUpdated</c>. Deleting or
/// recategorizing a transaction does NOT retroactively adjust
/// <c>BudgetPeriod.Spent</c>; drift accumulates until a manual rebuild path
/// lands. Tracked under "Known rough edges" in <c>BACKEND.md</c>.</para>
/// </remarks>
internal sealed class UpdateBudgetPeriodOnTransactionCreatedHandler(
    IApplicationDbContext db,
    ILogger<UpdateBudgetPeriodOnTransactionCreatedHandler> logger)
    : IDomainEventHandler<TransactionCreatedDomainEvent>
{
    public async Task Handle(TransactionCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Budget handler received TransactionCreated {TransactionId} (categoryId={CategoryId}, isTransfer={IsTransfer}, isAdjustment={IsAdjustment}, direction={Direction})",
            domainEvent.TransactionId,
            domainEvent.CategoryId,
            domainEvent.IsTransfer,
            domainEvent.IsAdjustment,
            domainEvent.Direction);

        if (ShouldSkip(domainEvent))
        {
            return;
        }

        // CategoryId non-null is guaranteed by ShouldSkip above; pull it
        // once so the LINQ below doesn't lift through the nullable.
        Guid categoryId = domainEvent.CategoryId!.Value;

        Budget? budget = await db.Budgets
            .FirstOrDefaultAsync(b => b.CategoryId == categoryId, cancellationToken);

        if (budget is null)
        {
            // Categories without a configured budget are the common case;
            // returning silently keeps the import path cheap.
            return;
        }

        int year = domainEvent.TransactionDate.Year;
        int month = domainEvent.TransactionDate.Month;

        BudgetPeriod? period = await db.BudgetPeriods
            .FirstOrDefaultAsync(
                p => p.BudgetId == budget.Id && p.Year == year && p.Month == month,
                cancellationToken);

        if (period is null)
        {
            Result<BudgetPeriod> periodResult = BudgetPeriod.Create(budget.Id, year, month);
            // Year/month came from the event's TransactionDate, which the
            // Transaction factory already validated - the guard is defensive and
            // throws loudly during dev if a future change ever breaks that.
            BudgetPeriodGuard.EnsureSucceeded(
                periodResult, $"Failed to create BudgetPeriod for budget {budget.Id}");

            period = periodResult.Value;
            db.BudgetPeriods.Add(period);
        }

        // AmountMdl > 0 is filtered by ShouldSkip; the guard is defensive.
        BudgetPeriodGuard.EnsureSucceeded(
            period.AddSpend(domainEvent.AmountMdl!.Value),
            $"Failed to add spend to BudgetPeriod {period.Id}");

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool ShouldSkip(TransactionCreatedDomainEvent evt) =>
        evt.CategoryId is null
        || evt.IsTransfer
        || evt.IsAdjustment
        || evt.Direction != TransactionDirection.Expense
        || evt.AmountMdl is null
        || evt.AmountMdl <= 0m;
}
