using FluentValidation;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.UpdateCategory;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(c => c.Id)
            .NotEmpty();

        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(Category.NameMaxLength);

        RuleFor(c => c.Flow)
            .IsInEnum();

        RuleFor(c => c.Color)
            .Matches("^#[0-9A-Fa-f]{6}$")
            .When(c => c.Color is not null);
    }
}
