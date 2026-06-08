using FluentValidation;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.UpdateCategoryPattern;

public sealed class UpdateCategoryPatternCommandValidator : AbstractValidator<UpdateCategoryPatternCommand>
{
    public UpdateCategoryPatternCommandValidator()
    {
        RuleFor(c => c.Keyword)
            .NotEmpty()
            .MaximumLength(CategoryPattern.KeywordMaxLength);

        RuleFor(c => c.CategoryId)
            .NotEmpty();
    }
}
