using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.CreateBudget;

internal sealed class CreateBudgetCommandHandler(
    IApplicationDbContext db) : ICommandHandler<CreateBudgetCommand, CreateBudgetResponse>
{
    public async Task<Result<CreateBudgetResponse>> Handle(
        CreateBudgetCommand command,
        CancellationToken cancellationToken)
    {
        bool categoryExists = await db.Categories
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<CreateBudgetResponse>(CategoryErrors.NotFound(command.CategoryId));
        }

        // EF Core's HasQueryFilter on Budget excludes archived rows already,
        // so this AnyAsync only sees active budgets - exactly the conflict we
        // want to catch. The explicit !IsArchived predicate is defense-in-depth
        // for unit tests (which bypass model configuration) and mirrors
        // GetSummaryQueryHandler. The DB-side filtered unique index is the backup.
        bool alreadyHasActive = await db.Budgets
            .Where(b => !b.IsArchived)
            .AnyAsync(b => b.CategoryId == command.CategoryId, cancellationToken);

        if (alreadyHasActive)
        {
            return Result.Failure<CreateBudgetResponse>(
                BudgetErrors.AlreadyExistsForCategory(command.CategoryId));
        }

        var monthlyLimit = new Money(command.MonthlyLimit, ReportingCurrencies.Mdl);
        Result<Budget> budgetResult = Budget.Create(command.CategoryId, monthlyLimit);

        if (budgetResult.IsFailure)
        {
            return Result.Failure<CreateBudgetResponse>(budgetResult.Error);
        }

        Budget budget = budgetResult.Value;
        db.Budgets.Add(budget);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBudgetResponse(budget.Id);
    }
}
