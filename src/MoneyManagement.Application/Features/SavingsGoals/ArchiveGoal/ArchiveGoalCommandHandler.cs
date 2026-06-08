using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.SavingsGoals.ArchiveGoal;

internal sealed class ArchiveGoalCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchiveGoalCommand>
{
    public async Task<Result> Handle(ArchiveGoalCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so the same command can re-archive (idempotent
        // contract). The default filter hides archived goals; without this
        // a second DELETE call would 404 instead of being a no-op. Mirrors
        // the Budget archive handler.
        SavingsGoal? goal = await db.SavingsGoals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (goal is null)
        {
            return Result.Failure(SavingsGoalErrors.NotFound(command.Id));
        }

        Result archive = goal.Archive();
        if (archive.IsFailure)
        {
            return archive;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
