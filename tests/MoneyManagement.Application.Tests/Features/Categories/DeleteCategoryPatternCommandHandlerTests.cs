using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Categories.DeleteCategoryPattern;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class DeleteCategoryPatternCommandHandlerTests
{
    private static readonly Guid CategoryId = new("00000000-0000-0000-0000-000000000001");

    private static CategoryPattern NewPattern(string keyword = "LINELLA")
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create(keyword, CategoryId, CategoryPatternSource.Learned);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithExistingPattern_RemovesAndSucceeds()
    {
        CategoryPattern pattern = NewPattern();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categoryPatterns: [pattern]);

        var handler = new DeleteCategoryPatternCommandHandler(db);

        Result result = await handler.Handle(new DeleteCategoryPatternCommand(pattern.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.CategoryPatterns.Should().NotContain(pattern);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingPattern_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        var handler = new DeleteCategoryPatternCommandHandler(db);
        var unknownId = Guid.NewGuid();

        Result result = await handler.Handle(new DeleteCategoryPatternCommand(unknownId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.PatternNotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
