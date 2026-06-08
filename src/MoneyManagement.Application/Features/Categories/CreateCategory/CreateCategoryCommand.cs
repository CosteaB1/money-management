using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories.CreateCategory;

public sealed record CreateCategoryCommand(
    string Name,
    CategoryFlow Flow,
    Guid? ParentId,
    string? Color,
    string? Icon) : ICommand<Guid>;
