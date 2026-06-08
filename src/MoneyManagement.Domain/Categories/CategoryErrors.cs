using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Categories;

public static class CategoryErrors
{
    public static readonly Error NameRequired =
        Error.Validation("category.name_required", "Category name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("category.name_too_long", "Category name must be 80 characters or fewer.");

    public static readonly Error InvalidColor =
        Error.Validation("category.invalid_color", "Color must be a hex string in the form #RRGGBB.");

    public static readonly Error IconTooLong =
        Error.Validation("category.icon_too_long", "Icon must be 40 characters or fewer.");

    public static readonly Error InvalidFlow =
        Error.Validation("category.invalid_flow", "Category flow is not a valid value.");

    public static readonly Error PatternKeywordRequired =
        Error.Validation("category.pattern_keyword_required", "Pattern keyword is required.");

    public static readonly Error PatternKeywordTooLong =
        Error.Validation("category.pattern_keyword_too_long", "Pattern keyword must be 100 characters or fewer.");

    public static readonly Error PatternCategoryRequired =
        Error.Validation("category.pattern_category_required", "Pattern must reference a category.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("category.not_found", $"Category with id '{id}' was not found.");

    public static Error ParentNotFound(Guid id) =>
        Error.NotFound("category.parent_not_found", $"Parent category with id '{id}' was not found.");

    public static Error PatternNotFound(Guid id) =>
        Error.NotFound("category.pattern_not_found", $"Category pattern with id '{id}' was not found.");

    public static Error PatternKeywordExists(string keyword) =>
        Error.Conflict("category.pattern_keyword_exists", $"A category pattern for keyword '{keyword}' already exists.");
}
