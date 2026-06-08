using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.UpdateCategory;

public sealed record UpdateCategoryCommand(
    Guid Id,
    string Name,
    CategoryFlow Flow,
    string? Color) : ICommand;
