using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Categories.UpdateCategoryPattern;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class UpdateCategoryPatternCommandHandlerTests
{
    private static Category NewCategory(string name = "Groceries")
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static CategoryPattern NewPattern(string keyword, Guid categoryId)
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create(keyword, categoryId, CategoryPatternSource.Learned);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithValidCommand_UpdatesKeywordAndCategory()
    {
        Category groceries = NewCategory("Groceries");
        Category dining = NewCategory("Dining");
        CategoryPattern pattern = NewPattern("LINELLA", groceries.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries, dining],
            categoryPatterns: [pattern]);

        var handler = new UpdateCategoryPatternCommandHandler(db);
        var command = new UpdateCategoryPatternCommand(pattern.Id, "  kfc  ", dining.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        pattern.Keyword.Should().Be("KFC");
        pattern.CategoryId.Should().Be(dining.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithCollidingKeyword_ReturnsConflict()
    {
        Category category = NewCategory();
        CategoryPattern target = NewPattern("LINELLA", category.Id);
        CategoryPattern other = NewPattern("KFC", category.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            categoryPatterns: [target, other]);

        var handler = new UpdateCategoryPatternCommandHandler(db);
        // Renaming target to an existing other-pattern keyword collides.
        var command = new UpdateCategoryPatternCommand(target.Id, "kfc", category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordExists("KFC"));
        target.Keyword.Should().Be("LINELLA");
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithSameKeywordExcludingSelf_Succeeds()
    {
        Category category = NewCategory();
        CategoryPattern pattern = NewPattern("LINELLA", category.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            categoryPatterns: [pattern]);

        var handler = new UpdateCategoryPatternCommandHandler(db);
        // No-op keyword edit must not collide with itself.
        var command = new UpdateCategoryPatternCommand(pattern.Id, "linella", category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        pattern.Keyword.Should().Be("LINELLA");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingPattern_ReturnsNotFound()
    {
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);

        var handler = new UpdateCategoryPatternCommandHandler(db);
        var unknownId = Guid.NewGuid();
        var command = new UpdateCategoryPatternCommand(unknownId, "KFC", category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternNotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingCategory_ReturnsNotFound()
    {
        Category category = NewCategory();
        CategoryPattern pattern = NewPattern("LINELLA", category.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            categoryPatterns: [pattern]);

        var handler = new UpdateCategoryPatternCommandHandler(db);
        var unknownCategoryId = Guid.NewGuid();
        var command = new UpdateCategoryPatternCommand(pattern.Id, "KFC", unknownCategoryId);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NotFound(unknownCategoryId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenDomainUpdateFails_PropagatesFailure_WithoutSaving()
    {
        // The pattern and category both exist and the (empty) keyword doesn't
        // collide, but CategoryPattern.Update rejects a blank keyword. The
        // command validator normally blocks this, but at the handler level the
        // domain guard is the last line of defence and must propagate.
        Category category = NewCategory();
        CategoryPattern pattern = NewPattern("LINELLA", category.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            categoryPatterns: [pattern]);

        var handler = new UpdateCategoryPatternCommandHandler(db);
        var command = new UpdateCategoryPatternCommand(pattern.Id, "   ", category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternKeywordRequired);
        pattern.Keyword.Should().Be("LINELLA");
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
