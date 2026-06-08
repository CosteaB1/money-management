using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Transactions.UpdateTransactionCategory;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class UpdateTransactionCategoryCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Transaction NewTransaction(
        TransactionDirection direction = TransactionDirection.Expense,
        Guid? categoryId = null)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            direction,
            new Money(100m, ReportingCurrencies.Mdl),
            "Coffee",
            TransactionSource.Manual,
            categoryId: categoryId);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Category NewCategory(CategoryFlow flow, string name = "Groceries")
    {
        Result<Category> result = Category.Create(name, flow);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithCompatibleCategory_AssignsCategory()
    {
        Transaction transaction = NewTransaction(TransactionDirection.Expense);
        Category category = NewCategory(CategoryFlow.Expense);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            transactions: [transaction]);

        var handler = new UpdateTransactionCategoryCommandHandler(db, FakeFxConverter.Identity());
        var command = new UpdateTransactionCategoryCommand(transaction.Id, category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.CategoryId.Should().Be(category.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithBothFlowCategory_AssignsRegardlessOfDirection()
    {
        Transaction transaction = NewTransaction(TransactionDirection.Income);
        Category category = NewCategory(CategoryFlow.Both);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            transactions: [transaction]);

        var handler = new UpdateTransactionCategoryCommandHandler(db, FakeFxConverter.Identity());
        var command = new UpdateTransactionCategoryCommand(transaction.Id, category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.CategoryId.Should().Be(category.Id);
    }

    [Fact]
    public async Task Handle_WithNullCategory_ClearsCategory()
    {
        var existingCategoryId = Guid.CreateVersion7();
        Transaction transaction = NewTransaction(TransactionDirection.Expense, categoryId: existingCategoryId);
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        var handler = new UpdateTransactionCategoryCommandHandler(db, FakeFxConverter.Identity());
        var command = new UpdateTransactionCategoryCommand(transaction.Id, CategoryId: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.CategoryId.Should().BeNull();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingTransaction_ReturnsNotFound()
    {
        Category category = NewCategory(CategoryFlow.Expense);
        IApplicationDbContext db = FakeApplicationDbContext.Create(categories: [category]);

        var handler = new UpdateTransactionCategoryCommandHandler(db, FakeFxConverter.Identity());
        var unknownId = Guid.CreateVersion7();
        var command = new UpdateTransactionCategoryCommand(unknownId, category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.NotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingCategory_ReturnsNotFound()
    {
        Transaction transaction = NewTransaction(TransactionDirection.Expense);
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        var handler = new UpdateTransactionCategoryCommandHandler(db, FakeFxConverter.Identity());
        var unknownCategoryId = Guid.CreateVersion7();
        var command = new UpdateTransactionCategoryCommand(transaction.Id, unknownCategoryId);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NotFound(unknownCategoryId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvokesFxConverterWithTransactionDateAndCurrency()
    {
        var txDate = new DateOnly(2026, 4, 15);
        Result<Transaction> txResult = Transaction.Create(
            AccountId,
            txDate,
            TransactionDirection.Expense,
            new Money(120m, "USD"),
            "Coffee abroad",
            TransactionSource.Manual);
        txResult.IsSuccess.Should().BeTrue();
        Transaction transaction = txResult.Value;

        Category category = NewCategory(CategoryFlow.Expense);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            transactions: [transaction]);

        IFxConverter fx = FakeFxConverter.Identity();
        var handler = new UpdateTransactionCategoryCommandHandler(db, fx);
        var command = new UpdateTransactionCategoryCommand(transaction.Id, category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await fx.Received(1).ConvertAsync(
            120m,
            "USD",
            ReportingCurrencies.Mdl,
            txDate,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithIncompatibleFlow_ReturnsCategoryFlowMismatch()
    {
        // Expense transaction against an income-only category.
        Transaction transaction = NewTransaction(TransactionDirection.Expense);
        Category category = NewCategory(CategoryFlow.Income, name: "Salary");
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            transactions: [transaction]);

        var handler = new UpdateTransactionCategoryCommandHandler(db, FakeFxConverter.Identity());
        var command = new UpdateTransactionCategoryCommand(transaction.Id, category.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.CategoryFlowMismatch);
        transaction.CategoryId.Should().BeNull();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
