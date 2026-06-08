using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Budgets.CreateBudget;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Budgets;

public class CreateBudgetCommandHandlerTests
{
    private static Category NewCategory(string name = "Groceries")
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsId()
    {
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);
        var handler = new CreateBudgetCommandHandler(db);

        Result<CreateBudgetResponse> result = await handler.Handle(
            new CreateBudgetCommand(category.Id, 1_500m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Budget persisted = db.Budgets.Single();
        persisted.CategoryId.Should().Be(category.Id);
        persisted.MonthlyLimit.Amount.Should().Be(1_500m);
        persisted.MonthlyLimit.Currency.Should().Be("MDL");
        persisted.IsArchived.Should().BeFalse();
        result.Value.Id.Should().Be(persisted.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNonPositiveLimit_ReturnsDomainFailure_AndDoesNotSave()
    {
        // Category exists and no active budget collides, so the handler reaches
        // Budget.Create — which rejects the non-positive limit (validator-shadowed).
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);
        var handler = new CreateBudgetCommandHandler(db);

        Result<CreateBudgetResponse> result = await handler.Handle(
            new CreateBudgetCommand(category.Id, 0m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.LimitMustBePositive);
        db.Budgets.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingCategory_ReturnsCategoryNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateBudgetCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result<CreateBudgetResponse> result = await handler.Handle(
            new CreateBudgetCommand(missingId, 1_000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NotFound(missingId));
        db.Budgets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithExistingActiveBudgetForCategory_ReturnsConflict()
    {
        Category category = NewCategory();
        Budget existing = Budget.Create(category.Id, new Money(2_000m, "MDL")).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            budgets: [existing]);

        var handler = new CreateBudgetCommandHandler(db);

        Result<CreateBudgetResponse> result = await handler.Handle(
            new CreateBudgetCommand(category.Id, 3_000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.AlreadyExistsForCategory(category.Id));
        db.Budgets.Count().Should().Be(1);
    }
}
