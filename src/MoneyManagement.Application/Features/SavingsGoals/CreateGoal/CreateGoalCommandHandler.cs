using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.SavingsGoals.CreateGoal;

internal sealed class CreateGoalCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : ICommandHandler<CreateGoalCommand, CreateGoalResponse>
{
    public async Task<Result<CreateGoalResponse>> Handle(
        CreateGoalCommand command,
        CancellationToken cancellationToken)
    {
        if (command.LinkedAccountId is Guid linkedId)
        {
            // The HasQueryFilter on Account hides archived accounts; a goal
            // pointing at an archived account would be useless. AnyAsync via
            // the default filter rejects both archived and missing ids with
            // the same NotFound error - consistent with how the rest of the
            // app treats archived accounts.
            bool accountExists = await db.Accounts
                .AnyAsync(a => a.Id == linkedId, cancellationToken);

            if (!accountExists)
            {
                return Result.Failure<CreateGoalResponse>(AccountErrors.NotFound(linkedId));
            }
        }

        var target = new Money(command.TargetAmount, ReportingCurrencies.Mdl);
        Result<SavingsGoal> goalResult = SavingsGoal.Create(
            command.Name,
            target,
            command.TargetDate,
            command.LinkedAccountId,
            clock);

        if (goalResult.IsFailure)
        {
            return Result.Failure<CreateGoalResponse>(goalResult.Error);
        }

        SavingsGoal goal = goalResult.Value;
        db.SavingsGoals.Add(goal);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateGoalResponse(goal.Id);
    }
}
