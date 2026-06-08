using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.SavingsGoals.UpdateGoal;

internal sealed class UpdateGoalCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : ICommandHandler<UpdateGoalCommand>
{
    public async Task<Result> Handle(UpdateGoalCommand command, CancellationToken cancellationToken)
    {
        SavingsGoal? goal = await db.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (goal is null)
        {
            return Result.Failure(SavingsGoalErrors.NotFound(command.Id));
        }

        if (command.LinkedAccountId is Guid linkedId)
        {
            // Same archived/missing semantics as CreateGoal - the default
            // query filter on Account excludes archived rows so a switch
            // to an archived account is rejected with NotFound.
            bool accountExists = await db.Accounts
                .AnyAsync(a => a.Id == linkedId, cancellationToken);

            if (!accountExists)
            {
                return Result.Failure(AccountErrors.NotFound(linkedId));
            }
        }

        Result rename = goal.Rename(command.Name);
        if (rename.IsFailure)
        {
            return rename;
        }

        var newTarget = new Money(command.TargetAmount, ReportingCurrencies.Mdl);
        Result updateTarget = goal.UpdateTarget(newTarget);
        if (updateTarget.IsFailure)
        {
            return updateTarget;
        }

        Result updateDate = goal.UpdateTargetDate(command.TargetDate, clock);
        if (updateDate.IsFailure)
        {
            return updateDate;
        }

        // Mode-switch comes last so a validation failure above leaves the
        // existing link/manual state untouched. Only switch when the mode
        // actually CHANGES: re-running Unlink()/LinkAccount() on a goal that is
        // already in the target mode would wipe the user's manual-saved amount
        // (Unlink resets it to zero), so editing a manual goal's name/target
        // must not touch its saved progress.
        if (command.LinkedAccountId is Guid newLinkId)
        {
            // Target = linked. Only (re)link when currently unlinked or pointed
            // at a different account; re-linking the same account is a no-op.
            if (goal.LinkedAccountId != newLinkId)
            {
                Result link = goal.LinkAccount(newLinkId);
                if (link.IsFailure)
                {
                    return link;
                }
            }
        }
        else if (goal.LinkedAccountId is not null)
        {
            // Target = manual and the goal is currently linked: a real
            // linked -> manual switch, so the reset-to-zero is correct. An
            // already-manual goal falls through untouched, preserving its saved
            // amount.
            Result unlink = goal.Unlink();
            if (unlink.IsFailure)
            {
                return unlink;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
