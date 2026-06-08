using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Categories.GetCategories;

public sealed record GetCategoriesQuery(bool IncludeArchived = false)
    : IQuery<IReadOnlyList<CategoryDto>>;
