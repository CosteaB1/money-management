using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Categories;

/// <summary>
/// A keyword-to-category rule consumed by the import category suggester. The
/// <see cref="Keyword"/> is stored upper-cased so matching is case-insensitive
/// via a plain <c>Contains</c> against an upper-cased description.
/// </summary>
public sealed class CategoryPattern : Entity
{
    public const int KeywordMaxLength = 100;

    // EF Core
    private CategoryPattern() { }

    private CategoryPattern(Guid id, string keyword, Guid categoryId, CategoryPatternSource source)
        : base(id)
    {
        Keyword = keyword;
        CategoryId = categoryId;
        Source = source;
    }

    public string Keyword { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public CategoryPatternSource Source { get; private set; }

    public static Result<CategoryPattern> Create(
        string keyword,
        Guid categoryId,
        CategoryPatternSource source)
    {
        Result<string> validation = ValidateKeyword(keyword, categoryId);
        if (validation.IsFailure)
        {
            return Result.Failure<CategoryPattern>(validation.Error);
        }

        return new CategoryPattern(Guid.CreateVersion7(), validation.Value, categoryId, source);
    }

    /// <summary>
    /// Re-points the pattern at a (possibly different) category and replaces its
    /// keyword, applying the same trim/upper-case/length rules as <see cref="Create"/>.
    /// </summary>
    public Result Update(string keyword, Guid categoryId)
    {
        Result<string> validation = ValidateKeyword(keyword, categoryId);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        Keyword = validation.Value;
        CategoryId = categoryId;

        return Result.Success();
    }

    /// <summary>
    /// Shared keyword/category validation. On success returns the normalized
    /// (trimmed + upper-cased) keyword.
    /// </summary>
    private static Result<string> ValidateKeyword(string keyword, Guid categoryId)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Result.Failure<string>(CategoryErrors.PatternKeywordRequired);
        }

        string normalized = keyword.Trim().ToUpperInvariant();

        if (normalized.Length > KeywordMaxLength)
        {
            return Result.Failure<string>(CategoryErrors.PatternKeywordTooLong);
        }

        if (categoryId == Guid.Empty)
        {
            return Result.Failure<string>(CategoryErrors.PatternCategoryRequired);
        }

        return normalized;
    }
}
