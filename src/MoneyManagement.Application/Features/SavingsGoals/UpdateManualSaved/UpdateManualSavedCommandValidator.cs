using FluentValidation;

namespace MoneyManagement.Application.Features.SavingsGoals.UpdateManualSaved;

public sealed class UpdateManualSavedCommandValidator : AbstractValidator<UpdateManualSavedCommand>
{
    public UpdateManualSavedCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Amount).GreaterThanOrEqualTo(0m);
    }
}
