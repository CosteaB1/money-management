using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.EventHandlers;

/// <summary>
/// Reacts to a <see cref="Transaction"/> moving categories. Subtracts the
/// row's MDL value from the previous category's <see cref="BudgetPeriod"/>
/// (if any) and adds it to the new category's period (find-or-create) so the
/// rollup stays in sync with a recategorization. One save at the end covers
/// both mutations.
/// </summary>
/// <remarks>
/// <para>Shared skip rules — transfers, adjustments, income, and rows with no
/// usable MDL value are no-ops on both sides. The aggregate's
/// <see cref="Transaction.SetCategory"/> guard already filters identical-id
/// calls, so the handler never sees an old == new event.</para>
/// </remarks>
internal sealed class UpdateBudgetPeriodOnTransactionCategoryChangedHandler(
    IApplicationDbContext db,
    ILogger<UpdateBudgetPeriodOnTransactionCategoryChangedHandler> logger)
    : IDomainEventHandler<TransactionCategoryChangedDomainEvent>
{
    public async Task Handle(TransactionCategoryChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Budget handler received TransactionCategoryChanged {TransactionId} (old={OldCategoryId}, new={NewCategoryId}, isTransfer={IsTransfer}, isAdjustment={IsAdjustment}, direction={Direction})",
            domainEvent.TransactionId,
            domainEvent.OldCategoryId,
            domainEvent.NewCategoryId,
            domainEvent.IsTransfer,
            domainEvent.IsAdjustment,
            domainEvent.Direction);

        if (ShouldSkipCommon(domainEvent))
        {
            return;
        }

        decimal amountMdl = domainEvent.AmountMdl!.Value;

        // One query covers both sides — at most two budget rows, so the
        // OR-predicate stays trivial and we avoid a second round-trip.
        Guid? oldCategoryId = domainEvent.OldCategoryId;
        Guid? newCategoryId = domainEvent.NewCategoryId;

        List<Budget> budgets = await db.Budgets
            .Where(b =>
                oldCategoryId != null && b.CategoryId == oldCategoryId.Value
                || newCategoryId != null && b.CategoryId == newCategoryId.Value)
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
        {
            return;
        }

        Budget? oldBudget = oldCategoryId is { } oldId
            ? budgets.FirstOrDefault(b => b.CategoryId == oldId)
            : null;
        Budget? newBudget = newCategoryId is { } newId
            ? budgets.FirstOrDefault(b => b.CategoryId == newId)
            : null;

        int year = domainEvent.TransactionDate.Year;
        int month = domainEvent.TransactionDate.Month;
        bool mutated = false;

        if (oldBudget is not null)
        {
            BudgetPeriod? oldPeriod = await db.BudgetPeriods
                .FirstOrDefaultAsync(
                    p => p.BudgetId == oldBudget.Id && p.Year == year && p.Month == month,
                    cancellationToken);

            if (oldPeriod is not null)
            {
                BudgetPeriodGuard.EnsureSucceeded(
                    oldPeriod.SubtractSpend(amountMdl),
                    $"Failed to subtract spend from BudgetPeriod {oldPeriod.Id}");

                mutated = true;
            }
        }

        if (newBudget is not null)
        {
            BudgetPeriod? newPeriod = await db.BudgetPeriods
                .FirstOrDefaultAsync(
                    p => p.BudgetId == newBudget.Id && p.Year == year && p.Month == month,
                    cancellationToken);

            if (newPeriod is null)
            {
                Result<BudgetPeriod> created = BudgetPeriod.Create(newBudget.Id, year, month);
                BudgetPeriodGuard.EnsureSucceeded(
                    created, $"Failed to create BudgetPeriod for budget {newBudget.Id}");

                newPeriod = created.Value;
                db.BudgetPeriods.Add(newPeriod);
            }

            BudgetPeriodGuard.EnsureSucceeded(
                newPeriod.AddSpend(amountMdl),
                $"Failed to add spend to BudgetPeriod {newPeriod.Id}");

            mutated = true;
        }

        if (mutated)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool ShouldSkipCommon(TransactionCategoryChangedDomainEvent evt) =>
        evt.IsTransfer
        || evt.IsAdjustment
        || evt.Direction != TransactionDirection.Expense
        || evt.AmountMdl is null
        || evt.AmountMdl <= 0m;
}
