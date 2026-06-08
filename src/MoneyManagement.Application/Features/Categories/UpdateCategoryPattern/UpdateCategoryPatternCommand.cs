using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Categories.UpdateCategoryPattern;

public sealed record UpdateCategoryPatternCommand(Guid Id, string Keyword, Guid CategoryId) : ICommand;
