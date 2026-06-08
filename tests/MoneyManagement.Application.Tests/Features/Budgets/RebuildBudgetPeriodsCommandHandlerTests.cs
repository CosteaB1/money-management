using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Budgets.RebuildBudgetPeriods;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Budgets;

/// <summary>
/// Happy-path coverage for the canonical drift-correction escape hatch.
/// Mirrors the create-handler's filter discipline (Expense only, not transfer,
/// not adjustment, has a category) and uses
/// <see cref="FakeFxConverter.Identity"/> + <c>WithTable</c> for FX paths.
/// </summary>
public class RebuildBudgetPeriodsCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();

    private static Budget NewBudget(Guid categoryId, decimal limit = 1_000m) =>
        Budget.Create(categoryId, new Money(limit, "MDL")).Value;

    private static Transaction NewExpense(
        Guid categoryId,
        decimal amount,
        DateOnly date,
        string currency = "MDL",
        bool isTransfer = false,
        bool isAdjustment = false,
        TransactionDirection direction = TransactionDirection.Expense) =>
        Transaction.Create(
            AccountId,
            date,
            direction,
            new Money(amount, currency),
            "x",
            TransactionSource.Manual,
            categoryId: categoryId,
            isTransfer: isTransfer,
            isAdjustment: isAdjustment).Value;

    [Fact]
    public async Task Handle_SingleBudget_RecomputesPeriodsFromTransactions()
    {
        var cat = Guid.CreateVersion7();
        Budget budget = NewBudget(cat);
        Transaction t1 = NewExpense(cat, 100m, new DateOnly(2026, 5, 10));
        Transaction t2 = NewExpense(cat, 50m, new DateOnly(2026, 5, 20));
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            transactions: [t1, t2]);

        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(budget.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BudgetsRebuilt.Should().Be(1);
        result.Value.PeriodsAffected.Should().Be(1);
        db.BudgetPeriods.Should().HaveCount(1);
        BudgetPeriod period = db.BudgetPeriods.Single();
        period.BudgetId.Should().Be(budget.Id);
        period.Year.Should().Be(2026);
        period.Month.Should().Be(5);
        period.Spent.Amount.Should().Be(150m);
    }

    [Fact]
    public async Task Handle_AggregatesAcrossMultipleMonths()
    {
        var cat = Guid.CreateVersion7();
        Budget budget = NewBudget(cat);
        Transaction t1 = NewExpense(cat, 100m, new DateOnly(2026, 3, 10));
        Transaction t2 = NewExpense(cat, 50m, new DateOnly(2026, 3, 20));
        Transaction t3 = NewExpense(cat, 200m, new DateOnly(2026, 4, 5));
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            transactions: [t1, t2, t3]);

        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(budget.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsAffected.Should().Be(2);
        BudgetPeriod[] ordered = [.. db.BudgetPeriods.OrderBy(p => p.Month)];
        ordered[0].Month.Should().Be(3);
        ordered[0].Spent.Amount.Should().Be(150m);
        ordered[1].Month.Should().Be(4);
        ordered[1].Spent.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_RowsWithoutFxRate_AreSkipped()
    {
        var cat = Guid.CreateVersion7();
        Budget budget = NewBudget(cat);
        // First row is MDL (identity path); second is USD with no rate available.
        Transaction tMdl = NewExpense(cat, 100m, new DateOnly(2026, 5, 10));
        Transaction tUsd = NewExpense(cat, 25m, new DateOnly(2026, 5, 11), currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            transactions: [tMdl, tUsd]);

        // Identity converter returns null for cross-currency conversion.
        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(budget.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.BudgetPeriods.Should().HaveCount(1);
        db.BudgetPeriods.Single().Spent.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_AllBudgets_CountsAllRebuilds()
    {
        var catA = Guid.CreateVersion7();
        var catB = Guid.CreateVersion7();
        Budget bA = NewBudget(catA);
        Budget bB = NewBudget(catB);
        Transaction tA = NewExpense(catA, 100m, new DateOnly(2026, 5, 10));
        Transaction tB = NewExpense(catB, 200m, new DateOnly(2026, 5, 11));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [bA, bB],
            transactions: [tA, tB]);

        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(BudgetId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BudgetsRebuilt.Should().Be(2);
        result.Value.PeriodsAffected.Should().Be(2);
        db.BudgetPeriods.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_IgnoresTransfers_Adjustments_Income_AndUncategorized()
    {
        var cat = Guid.CreateVersion7();
        Budget budget = NewBudget(cat);
        Transaction realExpense = NewExpense(cat, 100m, new DateOnly(2026, 5, 10));
        Transaction transfer = NewExpense(cat, 50m, new DateOnly(2026, 5, 11), isTransfer: true);
        Transaction adjustment = NewExpense(cat, 25m, new DateOnly(2026, 5, 12), isAdjustment: true);
        // Income with same category — even though Salary-type categories pair Income with Both flow,
        // the rebuild only counts Direction == Expense.
        Transaction income = Transaction.Create(
            AccountId,
            new DateOnly(2026, 5, 13),
            TransactionDirection.Income,
            new Money(75m, "MDL"),
            "Salary",
            TransactionSource.Manual,
            categoryId: cat).Value;
        // Uncategorized — must not roll up against this budget's category id.
        Transaction uncategorized = Transaction.Create(
            AccountId,
            new DateOnly(2026, 5, 14),
            TransactionDirection.Expense,
            new Money(10m, "MDL"),
            "Misc",
            TransactionSource.Manual).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            transactions: [realExpense, transfer, adjustment, income, uncategorized]);

        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(budget.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.BudgetPeriods.Should().HaveCount(1);
        db.BudgetPeriods.Single().Spent.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_DeletesExistingDriftedPeriodsBeforeRecomputing()
    {
        var cat = Guid.CreateVersion7();
        Budget budget = NewBudget(cat);
        // Drifted period from before the fix landed; April has no real expenses
        // so the rebuild should drop it entirely.
        BudgetPeriod aprilDrift = BudgetPeriod.Create(budget.Id, 2026, 4).Value;
        aprilDrift.AddSpend(999m);
        Transaction may = NewExpense(cat, 100m, new DateOnly(2026, 5, 10));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            budgets: [budget],
            budgetPeriods: [aprilDrift],
            transactions: [may]);

        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(budget.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.BudgetPeriods.Should().HaveCount(1);
        BudgetPeriod period = db.BudgetPeriods.Single();
        period.Month.Should().Be(5);
        period.Spent.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_UnknownBudgetId_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new RebuildBudgetPeriodsCommandHandler(db, FakeFxConverter.Identity());
        var unknown = Guid.CreateVersion7();

        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(unknown),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.NotFound(unknown));
    }
}
