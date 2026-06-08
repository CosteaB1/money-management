using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Categories.UpdateCategory;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class UpdateCategoryCommandHandlerTests
{
    private static Category NewCategory(
        string name = "Food",
        CategoryFlow flow = CategoryFlow.Expense,
        string? color = null)
    {
        Result<Category> result = Category.Create(name, flow, color: color);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithValidCommand_UpdatesNameFlowAndColor()
    {
        Category category = NewCategory(color: "#16a34a");
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);

        var handler = new UpdateCategoryCommandHandler(db);
        var command = new UpdateCategoryCommand(category.Id, "Dining", CategoryFlow.Both, "#dc2626");

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("Dining");
        category.Flow.Should().Be(CategoryFlow.Both);
        category.Color.Should().Be("#dc2626");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingCategory_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        var handler = new UpdateCategoryCommandHandler(db);
        var unknownId = Guid.CreateVersion7();
        var command = new UpdateCategoryCommand(unknownId, "Dining", CategoryFlow.Expense, null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithBlankName_ReturnsNameRequiredWithoutSaving()
    {
        // The domain re-validates even if the request bypassed the FluentValidation pipe.
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);

        var handler = new UpdateCategoryCommandHandler(db);
        var command = new UpdateCategoryCommand(category.Id, "   ", CategoryFlow.Expense, null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NameRequired);
        category.Name.Should().Be("Food");
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
