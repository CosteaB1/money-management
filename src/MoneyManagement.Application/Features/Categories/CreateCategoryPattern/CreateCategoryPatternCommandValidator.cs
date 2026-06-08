using FluentValidation;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.CreateCategoryPattern;

public sealed class CreateCategoryPatternCommandValidator : AbstractValidator<CreateCategoryPatternCommand>
{
    public CreateCategoryPatternCommandValidator()
    {
        RuleFor(c => c.Keyword)
            .NotEmpty()
            .MaximumLength(CategoryPattern.KeywordMaxLength);

        RuleFor(c => c.CategoryId)
            .NotEmpty();
    }
}
