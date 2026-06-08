using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.UpdateBudgetLimit;

internal sealed class UpdateBudgetLimitCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateBudgetLimitCommand>
{
    public async Task<Result> Handle(
        UpdateBudgetLimitCommand command,
        CancellationToken cancellationToken)
    {
        Budget? budget = await db.Budgets
            .FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken);

        if (budget is null)
        {
            return Result.Failure(BudgetErrors.NotFound(command.Id));
        }

        var newLimit = new Money(command.MonthlyLimit, ReportingCurrencies.Mdl);
        Result update = budget.UpdateLimit(newLimit);

        if (update.IsFailure)
        {
            return update;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
