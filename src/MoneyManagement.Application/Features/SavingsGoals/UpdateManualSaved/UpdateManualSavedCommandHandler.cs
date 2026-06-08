using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.SavingsGoals.UpdateManualSaved;

internal sealed class UpdateManualSavedCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock)
    : ICommandHandler<UpdateManualSavedCommand>
{
    public async Task<Result> Handle(
        UpdateManualSavedCommand command,
        CancellationToken cancellationToken)
    {
        SavingsGoal? goal = await db.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (goal is null)
        {
            return Result.Failure(SavingsGoalErrors.NotFound(command.Id));
        }

        // Capture the previous saved amount BEFORE the domain mutation so we
        // can write a contribution row for the delta. Linked-mode goals have a
        // null ManualSavedAmount; the SetManualSaved call below will reject
        // them with NotInManualMode and short-circuit before we touch the
        // contributions table.
        decimal previousAmount = goal.ManualSavedAmount?.Amount ?? 0m;

        var newAmount = new Money(command.Amount, ReportingCurrencies.Mdl);
        Result update = goal.SetManualSaved(newAmount);
        if (update.IsFailure)
        {
            return update;
        }

        decimal delta = command.Amount - previousAmount;
        if (delta != 0m)
        {
            // Delta sign carries the semantic: positive = contribution, negative
            // = withdrawal. The contribution factory only rejects ZERO amounts,
            // so signed values round-trip cleanly into the time-series.
            var today = DateOnly.FromDateTime(clock.UtcNow);
            Result<SavingsGoalContribution> contribution = SavingsGoalContribution.Create(
                goal.Id,
                new Money(delta, ReportingCurrencies.Mdl),
                today,
                notes: null,
                clock);

            if (contribution.IsFailure)
            {
                return Result.Failure(contribution.Error);
            }

            db.SavingsGoalContributions.Add(contribution.Value);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
