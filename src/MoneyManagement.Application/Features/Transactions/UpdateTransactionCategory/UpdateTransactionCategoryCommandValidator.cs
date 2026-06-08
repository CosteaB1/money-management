using FluentValidation;

namespace MoneyManagement.Application.Features.Transactions.UpdateTransactionCategory;

public sealed class UpdateTransactionCategoryCommandValidator : AbstractValidator<UpdateTransactionCategoryCommand>
{
    public UpdateTransactionCategoryCommandValidator()
    {
        RuleFor(c => c.Id)
            .NotEmpty();
    }
}
