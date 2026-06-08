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
/// Behavior tests for the inverse-of-Create budget event handler. Mirrors
/// <see cref="UpdateBudgetPeriodOnTransactionCreatedHandlerTests"/>'s shape:
/// same skip rules, plus the clamp-at-zero subtraction.
/// </summary>
public class UpdateBudgetPeriodOnTransactionDeletedHandlerTests
{
    private static readonly Guid CategoryId = Guid.CreateVersion7();
    private static readonly DateOnly InsideMay = new(2026, 5, 15);

    private static Budget NewBudget(decimal limit = 1_000m, Guid? categoryId = null) =>
        Budget.Create(categoryId ?? CategoryId, new Money(limit, "MDL")).Value;

    private static BudgetPeriod NewPeriod(Guid budgetId, int year, int month, decimal spent)
    {
        BudgetPeriod period = BudgetPeriod.Create(budgetId, year, month).Value;
        if (spent > 0m)
        {
            period.AddSpend(spent);
        }
        return period;
    }

    private static TransactionDeletedDomainEvent NewEvent(
        Guid? categoryId = null,
        bool noCategory = false,
        DateOnly? transactionDate = null,
        decimal? amountMdl = 100m,
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

    private static UpdateBudgetPeriodOnTransactionDeletedHandler NewHandler(IApplicationDbContext db) =>
        new(db, NullLogger<UpdateBudgetPeriodOnTransactionDeletedHandler>.Instance);

    [Fact]
    public async Task Handle_WithExistingPeriod_DecrementsSpend()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 300m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);

        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: 100m), CancellationToken.None);

        period.Spent.Amount.Should().Be(200m);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSubtractWouldGoNegative_ClampsAtZero()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 50m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);

        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: 75m), CancellationToken.None);

        period.Spent.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_WithNoPeriodForMonth_ReturnsSilently()
    {
        Budget budget = NewBudget();
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNoBudgetForCategory_ReturnsSilently()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(), CancellationToken.None);

        db.BudgetPeriods.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsWhenCategoryIdIsNull()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 300m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(noCategory: true), CancellationToken.None);

        period.Spent.Amount.Should().Be(300m);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsWhenIsTransfer()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 300m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(isTransfer: true), CancellationToken.None);

        period.Spent.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Handle_SkipsWhenIsAdjustment()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 300m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(isAdjustment: true), CancellationToken.None);

        period.Spent.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Handle_SkipsWhenIncome()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 300m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(direction: TransactionDirection.Income), CancellationToken.None);

        period.Spent.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Handle_SkipsWhenAmountMdlIsNull()
    {
        Budget budget = NewBudget();
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, spent: 300m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [period]);
        UpdateBudgetPeriodOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(amountMdl: null), CancellationToken.None);

        period.Spent.Amount.Should().Be(300m);
    }
}
