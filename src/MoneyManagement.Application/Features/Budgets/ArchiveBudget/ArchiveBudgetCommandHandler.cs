using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets.ArchiveBudget;

internal sealed class ArchiveBudgetCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchiveBudgetCommand>
{
    public async Task<Result> Handle(ArchiveBudgetCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so the same command can re-archive (idempotent
        // contract). The default filter hides archived budgets; without this
        // a second DELETE call would 404 instead of being a no-op.
        Budget? budget = await db.Budgets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken);

        if (budget is null)
        {
            return Result.Failure(BudgetErrors.NotFound(command.Id));
        }

        Result archive = budget.Archive();
        if (archive.IsFailure)
        {
            return archive;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
