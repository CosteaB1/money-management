using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Categories.CreateCategory;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class CreateCategoryCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsCategoryAndReturnsId()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateCategoryCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("Groceries", CategoryFlow.Expense, ParentId: null, Color: "#16a34a", Icon: "cart"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        db.Categories.Should().ContainSingle().Which.Name.Should().Be("Groceries");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithExistingParent_PersistsChildCategory()
    {
        Category parent = Category.Create("Bills", CategoryFlow.Expense).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [parent]);
        var handler = new CreateCategoryCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("Utilities", CategoryFlow.Expense, ParentId: parent.Id, Color: null, Icon: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.Categories.Should().Contain(c => c.Name == "Utilities" && c.ParentId == parent.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingParent_ReturnsParentNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateCategoryCommandHandler(db);

        var missingParent = Guid.CreateVersion7();
        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("Sub", CategoryFlow.Expense, ParentId: missingParent, Color: null, Icon: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.ParentNotFound(missingParent));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDuplicateActiveNameAndFlow_ReturnsDuplicateName_CaseInsensitively()
    {
        Category existing = Category.Create("Nodesparks", CategoryFlow.Income).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [existing]);
        var handler = new CreateCategoryCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("NODESPARKS", CategoryFlow.Income, ParentId: null, Color: null, Icon: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.DuplicateName("NODESPARKS", CategoryFlow.Income));
        result.Error.Code.Should().Be("category.duplicate_name");
        db.Categories.Should().ContainSingle();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithSameNameButDifferentFlow_Succeeds()
    {
        Category existing = Category.Create("Nodesparks", CategoryFlow.Income).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [existing]);
        var handler = new CreateCategoryCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("Nodesparks", CategoryFlow.Expense, ParentId: null, Color: null, Icon: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.Categories.Should().Contain(c => c.Name == "Nodesparks" && c.Flow == CategoryFlow.Expense);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithSameNameAndFlowAsArchivedCategory_Succeeds()
    {
        Category archived = Category.Create("Nodesparks", CategoryFlow.Income).Value;
        archived.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [archived]);
        var handler = new CreateCategoryCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("Nodesparks", CategoryFlow.Income, ParentId: null, Color: null, Icon: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.Categories.Should().Contain(c => c.Name == "Nodesparks" && !c.IsArchived);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithInvalidDomainInput_ReturnsDomainFailure_AndDoesNotSave()
    {
        // An undefined flow passes the parent check but fails Category.Create —
        // exercising the handler's domain-failure branch (shadowed by the validator).
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateCategoryCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateCategoryCommand("Food", (CategoryFlow)999, ParentId: null, Color: null, Icon: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.InvalidFlow);
        db.Categories.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
