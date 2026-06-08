using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.GetCategoryPatterns;

public sealed record CategoryPatternDto(
    Guid Id,
    string Keyword,
    Guid CategoryId,
    string CategoryName,
    CategoryPatternSource Source);
