using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Categories.CreateCategoryPattern;

public sealed record CreateCategoryPatternCommand(string Keyword, Guid CategoryId) : ICommand<Guid>;
