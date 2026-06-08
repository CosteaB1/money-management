using FluentValidation;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.CreateCategory;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(Category.NameMaxLength);

        RuleFor(c => c.Flow)
            .IsInEnum();

        RuleFor(c => c.Color)
            .Matches("^#[0-9A-Fa-f]{6}$")
            .When(c => c.Color is not null);

        RuleFor(c => c.Icon)
            .MaximumLength(Category.IconMaxLength)
            .When(c => c.Icon is not null);
    }
}
