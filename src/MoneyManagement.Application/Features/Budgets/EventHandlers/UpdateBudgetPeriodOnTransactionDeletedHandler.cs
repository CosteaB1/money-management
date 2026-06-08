using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.EventHandlers;

/// <summary>
/// Inverse of <see cref="UpdateBudgetPeriodOnTransactionCreatedHandler"/>. When
/// a <see cref="Transaction"/> is soft-deleted, subtract its MDL value from
/// the matching <see cref="BudgetPeriod"/> so the running spend stays accurate.
/// </summary>
/// <remarks>
/// <para>Skip rules mirror the create handler exactly — uncategorized rows,
/// transfers, adjustments, income, and rows with no usable MDL value are all
/// no-ops. If the budget or its period for the row's month doesn't exist there
/// is nothing to subtract from, so the handler returns silently in both
/// cases.</para>
/// <para>The clamp-at-zero is in
/// <see cref="BudgetPeriod.SubtractSpend"/> — FX drift between create-time and
/// delete-time can produce a small negative; the canonical correction is the
/// <c>RebuildBudgetPeriods</c> escape hatch.</para>
/// </remarks>
internal sealed class UpdateBudgetPeriodOnTransactionDeletedHandler(
    IApplicationDbContext db,
    ILogger<UpdateBudgetPeriodOnTransactionDeletedHandler> logger)
    : IDomainEventHandler<TransactionDeletedDomainEvent>
{
    public async Task Handle(TransactionDeletedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Budget handler received TransactionDeleted {TransactionId} (categoryId={CategoryId}, isTransfer={IsTransfer}, isAdjustment={IsAdjustment}, direction={Direction})",
            domainEvent.TransactionId,
            domainEvent.CategoryId,
            domainEvent.IsTransfer,
            domainEvent.IsAdjustment,
            domainEvent.Direction);

        if (ShouldSkip(domainEvent))
        {
            return;
        }

        Guid categoryId = domainEvent.CategoryId!.Value;

        Budget? budget = await db.Budgets
            .FirstOrDefaultAsync(b => b.CategoryId == categoryId, cancellationToken);

        if (budget is null)
        {
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
            // Nothing to subtract from — the create handler must never have
            // ticked spend for this (budget, month) pair, e.g. budget was
            // created mid-month after this row landed. Return silently.
            return;
        }

        BudgetPeriodGuard.EnsureSucceeded(
            period.SubtractSpend(domainEvent.AmountMdl!.Value),
            $"Failed to subtract spend from BudgetPeriod {period.Id}");

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool ShouldSkip(TransactionDeletedDomainEvent evt) =>
        evt.CategoryId is null
        || evt.IsTransfer
        || evt.IsAdjustment
        || evt.Direction != TransactionDirection.Expense
        || evt.AmountMdl is null
        || evt.AmountMdl <= 0m;
}
