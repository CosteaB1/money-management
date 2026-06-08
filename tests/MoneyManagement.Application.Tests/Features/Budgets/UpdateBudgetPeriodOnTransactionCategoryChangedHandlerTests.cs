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
/// Behavior tests for the recategorize-spend handler. Asserts the two-sided
/// move semantics (subtract from old, add to new) and the shared skip rules.
/// The aggregate's <c>SetCategory</c> already filters identical-id no-ops, so
/// the handler never sees an old == new event — not covered here.
/// </summary>
public class UpdateBudgetPeriodOnTransactionCategoryChangedHandlerTests
{
    private static readonly DateOnly InsideMay = new(2026, 5, 15);

    private static Budget NewBudget(Guid categoryId, decimal limit = 1_000m) =>
        Budget.Create(categoryId, new Money(limit, "MDL")).Value;

    private static BudgetPeriod NewPeriod(Guid budgetId, int year, int month, decimal spent)
    {
        BudgetPeriod period = BudgetPeriod.Create(budgetId, year, month).Value;
        if (spent > 0m)
        {
            period.AddSpend(spent);
        }
        return period;
    }

    private static TransactionCategoryChangedDomainEvent NewEvent(
        Guid? oldCategoryId,
        Guid? newCategoryId,
        DateOnly? transactionDate = null,
        decimal? amountMdl = 100m,
        TransactionDirection direction = TransactionDirection.Expense,
        bool isTransfer = false,
        bool isAdjustment = false) => new(
            TransactionId: Guid.CreateVersion7(),
            OldCategoryId: oldCategoryId,
            NewCategoryId: newCategoryId,
            TransactionDate: transactionDate ?? InsideMay,
            AmountMdl: amountMdl,
            Direction: direction,
            IsTransfer: isTransfer,
            IsAdjustment: isAdjustment);

    private static UpdateBudgetPeriodOnTransactionCategoryChangedHandler NewHandler(IApplicationDbContext db) =>
        new(db, NullLogger<UpdateBudgetPeriodOnTransactionCategoryChangedHandler>.Instance);

    [Fact]
    public async Task Handle_OldAndNewBothBudgeted_MovesSpend()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        Budget newBudget = NewBudget(newCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget, newBudget],
            budgetPeriods: [oldPeriod]);

        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: 100m), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(150m);
        db.BudgetPeriods.Should().HaveCount(2);
        BudgetPeriod newPeriod = db.BudgetPeriods.Single(p => p.BudgetId == newBudget.Id);
        newPeriod.Spent.Amount.Should().Be(100m);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyOldBudgeted_SubtractsAndDoesNotCreateNewPeriod()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget],
            budgetPeriods: [oldPeriod]);

        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: 100m), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(150m);
        db.BudgetPeriods.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_OldToNull_OnlyOldBudgeted_Subtracts()
    {
        var oldCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget],
            budgetPeriods: [oldPeriod]);

        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: null, amountMdl: 100m), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(150m);
        db.BudgetPeriods.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_NullToNew_OnlyNewBudgeted_Adds()
    {
        var newCat = Guid.CreateVersion7();
        Budget newBudget = NewBudget(newCat);

        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [newBudget]);
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: null, newCategoryId: newCat, amountMdl: 100m), CancellationToken.None);

        db.BudgetPeriods.Should().HaveCount(1);
        db.BudgetPeriods.Single().Spent.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_NeitherBudgeted_ReturnsSilently()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(
            NewEvent(oldCategoryId: Guid.CreateVersion7(), newCategoryId: Guid.CreateVersion7(), amountMdl: 100m),
            CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OldHasNoPeriod_SkipsSubtractButStillAddsToNew()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        Budget newBudget = NewBudget(newCat);

        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [oldBudget, newBudget]);
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: 100m), CancellationToken.None);

        db.BudgetPeriods.Should().HaveCount(1);
        db.BudgetPeriods.Single().BudgetId.Should().Be(newBudget.Id);
        db.BudgetPeriods.Single().Spent.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_SkipsWhenIsTransfer()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        Budget newBudget = NewBudget(newCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget, newBudget],
            budgetPeriods: [oldPeriod]);
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: 100m, isTransfer: true), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(250m);
        db.BudgetPeriods.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_SkipsWhenIsAdjustment()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        Budget newBudget = NewBudget(newCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget, newBudget],
            budgetPeriods: [oldPeriod]);
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: 100m, isAdjustment: true), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(250m);
        db.BudgetPeriods.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_SkipsWhenIncome()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        Budget newBudget = NewBudget(newCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget, newBudget],
            budgetPeriods: [oldPeriod]);
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: 100m, direction: TransactionDirection.Income), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(250m);
        db.BudgetPeriods.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_SkipsWhenAmountMdlIsNull()
    {
        var oldCat = Guid.CreateVersion7();
        var newCat = Guid.CreateVersion7();
        Budget oldBudget = NewBudget(oldCat);
        Budget newBudget = NewBudget(newCat);
        BudgetPeriod oldPeriod = NewPeriod(oldBudget.Id, 2026, 5, spent: 250m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [oldBudget, newBudget],
            budgetPeriods: [oldPeriod]);
        UpdateBudgetPeriodOnTransactionCategoryChangedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(oldCategoryId: oldCat, newCategoryId: newCat, amountMdl: null), CancellationToken.None);

        oldPeriod.Spent.Amount.Should().Be(250m);
        db.BudgetPeriods.Should().HaveCount(1);
    }
}
