using FluentValidation;

namespace MoneyManagement.Application.Features.Budgets.UpdateBudgetLimit;

public sealed class UpdateBudgetLimitCommandValidator : AbstractValidator<UpdateBudgetLimitCommand>
{
    public UpdateBudgetLimitCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.MonthlyLimit).GreaterThan(0m);
    }
}
