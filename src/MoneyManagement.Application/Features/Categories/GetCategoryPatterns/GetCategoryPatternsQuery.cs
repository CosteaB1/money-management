using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Categories.GetCategoryPatterns;

public sealed record GetCategoryPatternsQuery : IQuery<IReadOnlyList<CategoryPatternDto>>;
