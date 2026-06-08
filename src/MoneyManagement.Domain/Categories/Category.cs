using System.Text.RegularExpressions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Categories;

public sealed partial class Category : Entity
{
    public const int NameMaxLength = 80;
    public const int IconMaxLength = 40;
    public const int ColorMaxLength = 7;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    // EF Core
    private Category() { }

    private Category(
        Guid id,
        string name,
        CategoryFlow flow,
        Guid? parentId,
        string? color,
        string? icon) : base(id)
    {
        Name = name;
        Flow = flow;
        ParentId = parentId;
        Color = color;
        Icon = icon;
        IsArchived = false;
    }

    public string Name { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public string? Color { get; private set; }
    public string? Icon { get; private set; }
    public CategoryFlow Flow { get; private set; }
    public bool IsArchived { get; private set; }

    public static Result<Category> Create(
        string name,
        CategoryFlow flow,
        Guid? parentId = null,
        string? color = null,
        string? icon = null)
    {
        Result validation = Validate(name, flow, color, icon);
        if (validation.IsFailure)
        {
            return Result.Failure<Category>(validation.Error);
        }

        return new Category(Guid.CreateVersion7(), name.Trim(), flow, parentId, color, icon);
    }

    // Edits the identifying attributes in place. Parent/icon are left untouched
    // here because the update endpoint only exposes name/flow/color (v1 scope).
    public Result Update(string name, CategoryFlow flow, string? color)
    {
        Result validation = Validate(name, flow, color, icon: null);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name.Trim();
        Flow = flow;
        Color = color;

        return Result.Success();
    }

    public void Archive() => IsArchived = true;

    private static Result Validate(string name, CategoryFlow flow, string? color, string? icon)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(CategoryErrors.NameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure(CategoryErrors.NameTooLong);
        }

        if (!Enum.IsDefined(flow))
        {
            return Result.Failure(CategoryErrors.InvalidFlow);
        }

        if (color is not null && !HexColorRegex().IsMatch(color))
        {
            return Result.Failure(CategoryErrors.InvalidColor);
        }

        if (icon is { Length: > IconMaxLength })
        {
            return Result.Failure(CategoryErrors.IconTooLong);
        }

        return Result.Success();
    }
}
