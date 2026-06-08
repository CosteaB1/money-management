using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Features.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    string? Color,
    string? Icon,
    CategoryFlow Flow,
    bool IsArchived);
