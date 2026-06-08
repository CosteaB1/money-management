using FluentValidation;

namespace MoneyManagement.Application.Features.Budgets.CreateBudget;

public sealed class CreateBudgetCommandValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetCommandValidator()
    {
        RuleFor(c => c.CategoryId).NotEqual(Guid.Empty);
        RuleFor(c => c.MonthlyLimit).GreaterThan(0m);
    }
}
