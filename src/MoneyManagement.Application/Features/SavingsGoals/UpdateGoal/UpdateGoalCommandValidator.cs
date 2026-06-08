using FluentValidation;
using MoneyManagement.Domain.SavingsGoals;

namespace MoneyManagement.Application.Features.SavingsGoals.UpdateGoal;

public sealed class UpdateGoalCommandValidator : AbstractValidator<UpdateGoalCommand>
{
    public UpdateGoalCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);

        RuleFor(c => c.Name)
            .NotNull()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.")
            .MaximumLength(SavingsGoal.NameMaxLength);

        RuleFor(c => c.TargetAmount).GreaterThan(0m);
    }
}
