using FluentValidation;
using MoneyManagement.Domain.SavingsGoals;

namespace MoneyManagement.Application.Features.SavingsGoals.CreateGoal;

public sealed class CreateGoalCommandValidator : AbstractValidator<CreateGoalCommand>
{
    public CreateGoalCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotNull()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.")
            .MaximumLength(SavingsGoal.NameMaxLength);

        RuleFor(c => c.TargetAmount).GreaterThan(0m);
    }
}
