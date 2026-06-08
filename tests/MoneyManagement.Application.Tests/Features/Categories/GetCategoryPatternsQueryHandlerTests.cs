using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Categories.GetCategoryPatterns;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class GetCategoryPatternsQueryHandlerTests
{
    private static Category NewCategory(string name, bool archived = false)
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        Category category = result.Value;
        if (archived)
        {
            category.Archive();
        }

        return category;
    }

    private static CategoryPattern NewPattern(string keyword, Guid categoryId)
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create(keyword, categoryId, CategoryPatternSource.Learned);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_ReturnsJoinedDtosSortedByKeyword()
    {
        Category groceries = NewCategory("Groceries");
        Category dining = NewCategory("Dining");
        CategoryPattern kfc = NewPattern("KFC", dining.Id);
        CategoryPattern linella = NewPattern("LINELLA", groceries.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries, dining],
            categoryPatterns: [kfc, linella]);

        var handler = new GetCategoryPatternsQueryHandler(db);
        Result<IReadOnlyList<CategoryPatternDto>> result =
            await handler.Handle(new GetCategoryPatternsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        // Ordered by Keyword: KFC before LINELLA.
        result.Value[0].Keyword.Should().Be("KFC");
        result.Value[0].CategoryName.Should().Be("Dining");
        result.Value[0].CategoryId.Should().Be(dining.Id);
        result.Value[0].Source.Should().Be(CategoryPatternSource.Learned);
        result.Value[1].Keyword.Should().Be("LINELLA");
        result.Value[1].CategoryName.Should().Be("Groceries");
    }

    [Fact]
    public async Task Handle_IncludesPatternsForArchivedCategories()
    {
        Category archived = NewCategory("Old category", archived: true);
        CategoryPattern pattern = NewPattern("LEGACY", archived.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [archived],
            categoryPatterns: [pattern]);

        var handler = new GetCategoryPatternsQueryHandler(db);
        Result<IReadOnlyList<CategoryPatternDto>> result =
            await handler.Handle(new GetCategoryPatternsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        CategoryPatternDto dto = result.Value.Single();
        dto.Keyword.Should().Be("LEGACY");
        dto.CategoryName.Should().Be("Old category");
    }
}
