using FluentAssertions;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Categories;

public class CategoryPatternTests
{
    private static readonly Guid CategoryId = new("00000000-0000-0000-0000-000000000001");

    [Fact]
    public void Create_WithValidInput_UpperCasesAndTrimsKeyword()
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create("  linella  ", CategoryId, CategoryPatternSource.Seeded);

        result.IsSuccess.Should().BeTrue();
        CategoryPattern pattern = result.Value;
        pattern.Keyword.Should().Be("LINELLA");
        pattern.CategoryId.Should().Be(CategoryId);
        pattern.Source.Should().Be(CategoryPatternSource.Seeded);
        pattern.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankKeyword_ReturnsPatternKeywordRequired(string keyword)
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create(keyword, CategoryId, CategoryPatternSource.Learned);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordRequired);
    }

    [Fact]
    public void Create_WithKeywordTooLong_ReturnsPatternKeywordTooLong()
    {
        string keyword = new('a', CategoryPattern.KeywordMaxLength + 1);

        Result<CategoryPattern> result =
            CategoryPattern.Create(keyword, CategoryId, CategoryPatternSource.Seeded);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordTooLong);
    }

    [Fact]
    public void Create_WithEmptyCategoryId_ReturnsPatternCategoryRequired()
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create("LINELLA", Guid.Empty, CategoryPatternSource.Seeded);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternCategoryRequired);
    }

    [Fact]
    public void Update_WithValidInput_RepointsKeywordAndCategory()
    {
        CategoryPattern pattern =
            CategoryPattern.Create("linella", CategoryId, CategoryPatternSource.Seeded).Value;
        var newCategoryId = new Guid("00000000-0000-0000-0000-000000000002");

        Result result = pattern.Update("  kaufland  ", newCategoryId);

        result.IsSuccess.Should().BeTrue();
        pattern.Keyword.Should().Be("KAUFLAND");
        pattern.CategoryId.Should().Be(newCategoryId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankKeyword_ReturnsFailureAndLeavesPatternUnchanged(string keyword)
    {
        CategoryPattern pattern =
            CategoryPattern.Create("linella", CategoryId, CategoryPatternSource.Seeded).Value;

        Result result = pattern.Update(keyword, CategoryId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordRequired);
        pattern.Keyword.Should().Be("LINELLA");
    }
}
