using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Categories.DeleteCategoryPattern;

public sealed record DeleteCategoryPatternCommand(Guid Id) : ICommand;
