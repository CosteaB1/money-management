using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Budgets.EventHandlers;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Budgets;

/// <summary>
/// Behavior tests for the project's first domain-event handler. The handler
/// is invoked by <c>DomainEventsDispatcher</c> after the transaction's
/// <c>SaveChanges</c> completes (same scope), so re-saving inside the handler
/// doesn't retrigger the same event - we test the side effects on the
/// <c>BudgetPeriod</c> table.
/// </summary>
public class UpdateBudgetPeriodOnTransactionCreatedHandlerTests
{
    private static readonly Guid CategoryId = Guid.CreateVersion7();
    private static readonly DateOnly InsideMay = new(2026, 5, 15);

    private static Budget NewBudget(decimal limit = 1_000m, Guid? categoryId = null) =>
        Budget.Create(categoryId ?? CategoryId, new Money(limit, "MDL")).Value;

    private static TransactionCreatedDomainEvent NewEvent(
        Guid? categoryId = null,
        bool noCategory = false,
        DateOnly? transactionDate = null,
        decimal? amountMdl = 250m,
        TransactionDirection direction = TransactionDirection.Expense,
        bool isTransfer = false,
        bool isAdjustment = false) => new(
            TransactionId: Guid.CreateVersion7(),
            CategoryId: noCategory ? null : (categoryId ?? CategoryId),
            TransactionDate: transactionDate ?? InsideMay,
            AmountMdl: amountMdl,
            Direction: direction,
            IsTransfer: isTransfer,
            IsAdjustment: isAdjustment);

    private static UpdateBudgetPeriodOnTransactionCreatedHandler NewHandler(IApplicationDbContext db) =>
        new(db, NullLogger<UpdateBudgetPeriodOnTransactionCreatedHandler>.Instance);

    [Fact]
    public async Task Handle_WithExpenseAndBudget_CreatesPeriodAndAccumulatesSpend()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);

        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: 250m), CancellationToken.None);

        db.BudgetPeriods.Should().HaveCount(1);
        BudgetPeriod period = db.BudgetPeriods.Single();
        period.BudgetId.Should().Be(budget.Id);
        period.Year.Should().Be(2026);
        period.Month.Should().Be(5);
        period.Spent.Amount.Should().Be(250m);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SecondEventInSameMonth_SumsIntoExistingPeriod()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: 250m), CancellationToken.None);
        await handler.Handle(NewEvent(amountMdl: 175.50m), CancellationToken.None);

        db.BudgetPeriods.Should().HaveCount(1);
        db.BudgetPeriods.Single().Spent.Amount.Should().Be(425.50m);
    }

    [Fact]
    public async Task Handle_EventInDifferentMonth_CreatesNewPeriod()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: 100m, transactionDate: new DateOnly(2026, 5, 10)), CancellationToken.None);
        await handler.Handle(NewEvent(amountMdl: 200m, transactionDate: new DateOnly(2026, 6, 5)), CancellationToken.None);

        db.BudgetPeriods.Should().HaveCount(2);
        BudgetPeriod[] ordered = [.. db.BudgetPeriods.OrderBy(p => p.Month)];
        ordered[0].Month.Should().Be(5);
        ordered[0].Spent.Amount.Should().Be(100m);
        ordered[1].Month.Should().Be(6);
        ordered[1].Spent.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_SkipsWhenCategoryIdIsNull()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(noCategory: true), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsWhenIsTransfer()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(isTransfer: true), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsWhenIsAdjustment()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(isAdjustment: true), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsWhenIncome()
    {
        // Budgets cap spending, not income - even a categorized income event
        // (e.g. category = "Salary") must not touch the budget period.
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(direction: TransactionDirection.Income), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsWhenAmountMdlIsNull()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: null), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoBudgetForCategory_ReturnsSilently()
    {
        // No budget configured for any category - common case.
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BudgetForDifferentCategory_NoMatch()
    {
        Budget otherBudget = NewBudget(1_000m, categoryId: Guid.CreateVersion7());
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [otherBudget]);
        UpdateBudgetPeriodOnTransactionCreatedHandler handler = NewHandler(db);

        // Event for a category that has no active budget.
        await handler.Handle(NewEvent(categoryId: Guid.CreateVersion7()), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
    }
}
