using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Categories.CreateCategoryPattern;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class CreateCategoryPatternCommandHandlerTests
{
    private static Category NewCategory(string name = "Groceries")
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithValidCommand_PersistsUpperCasedKeywordAsLearned()
    {
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);

        var handler = new CreateCategoryPatternCommandHandler(db);
        var command = new CreateCategoryPatternCommand("  linella  ", category.Id);

        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        CategoryPattern persisted = db.CategoryPatterns.Single();
        persisted.Id.Should().Be(result.Value);
        persisted.Keyword.Should().Be("LINELLA");
        persisted.CategoryId.Should().Be(category.Id);
        persisted.Source.Should().Be(CategoryPatternSource.Learned);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingCategory_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        var handler = new CreateCategoryPatternCommandHandler(db);
        var unknownCategoryId = Guid.NewGuid();
        var command = new CreateCategoryPatternCommand("LINELLA", unknownCategoryId);

        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NotFound(unknownCategoryId));
        db.CategoryPatterns.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDuplicateKeyword_ReturnsConflict()
    {
        Category category = NewCategory();
        CategoryPattern existing =
            CategoryPattern.Create("LINELLA", category.Id, CategoryPatternSource.Seeded).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            categoryPatterns: [existing]);

        var handler = new CreateCategoryPatternCommandHandler(db);
        // Mixed-case + whitespace must still collide once normalized.
        var command = new CreateCategoryPatternCommand("  linella  ", category.Id);

        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordExists("LINELLA"));
        db.CategoryPatterns.Should().HaveCount(1);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithKeywordTooLong_ReturnsDomainFailure_AndDoesNotSave()
    {
        // Passes the category + uniqueness checks but fails CategoryPattern.Create
        // (shadowed by the validator in the full pipeline).
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);

        var handler = new CreateCategoryPatternCommandHandler(db);
        string keyword = new string('K', CategoryPattern.KeywordMaxLength + 1);
        var command = new CreateCategoryPatternCommand(keyword, category.Id);

        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordTooLong);
        db.CategoryPatterns.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
