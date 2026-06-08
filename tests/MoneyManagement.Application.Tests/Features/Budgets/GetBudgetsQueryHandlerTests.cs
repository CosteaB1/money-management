using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Budgets;
using MoneyManagement.Application.Features.Budgets.GetBudgets;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Budgets;

public class GetBudgetsQueryHandlerTests
{
    private static IDateTimeProvider Clock(DateTime utcNow)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

    private static Category NewCategory(string name = "Groceries")
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Budget NewBudget(Guid categoryId, decimal limit)
    {
        return Budget.Create(categoryId, new Money(limit, "MDL")).Value;
    }

    private static BudgetPeriod NewPeriod(Guid budgetId, int year, int month, decimal spent)
    {
        BudgetPeriod p = BudgetPeriod.Create(budgetId, year, month).Value;
        if (spent > 0m)
        {
            p.AddSpend(spent);
        }
        return p;
    }

    [Fact]
    public async Task Handle_DefaultsToCurrentMonthFromClock()
    {
        Category category = NewCategory();
        Budget budget = NewBudget(category.Id, 1_000m);
        BudgetPeriod marchPeriod = NewPeriod(budget.Id, 2026, 3, 200m);
        BudgetPeriod aprilPeriod = NewPeriod(budget.Id, 2026, 4, 500m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            budgets: [budget],
            budgetPeriods: [marchPeriod, aprilPeriod]);

        // Default clock anchored to April.
        var handler = new GetBudgetsQueryHandler(db, Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(new GetBudgetsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        BudgetDto dto = result.Value[0];
        dto.Year.Should().Be(2026);
        dto.Month.Should().Be(4);
        dto.Spent.Should().Be(500m);
    }

    [Fact]
    public async Task Handle_MissingBudgetPeriod_ReturnsSpentZero()
    {
        Category category = NewCategory();
        Budget budget = NewBudget(category.Id, 1_000m);
        // No BudgetPeriod row for the queried month.

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            budgets: [budget]);

        var handler = new GetBudgetsQueryHandler(db, Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(
            new GetBudgetsQuery(2026, 5), CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Spent.Should().Be(0m);
        result.Value[0].Remaining.Should().Be(1_000m);
        result.Value[0].Status.Should().Be(BudgetStatus.OnTrack);
    }

    [Fact]
    public async Task Handle_ArchivedBudgetsAreExcluded()
    {
        Category catA = NewCategory("A");
        Category catB = NewCategory("B");
        Budget activeBudget = NewBudget(catA.Id, 1_000m);
        Budget archivedBudget = NewBudget(catB.Id, 2_000m);
        archivedBudget.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [catA, catB],
            budgets: [activeBudget, archivedBudget]);

        var handler = new GetBudgetsQueryHandler(db, Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(
            new GetBudgetsQuery(2026, 5), CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Id.Should().Be(activeBudget.Id);
    }

    [Theory]
    [InlineData(0, BudgetStatus.OnTrack)]
    [InlineData(799, BudgetStatus.OnTrack)]       // 79.9% — below 80%
    [InlineData(800, BudgetStatus.Warning)]       // exactly 80%
    [InlineData(950, BudgetStatus.Warning)]       // 95%
    [InlineData(1_000, BudgetStatus.Warning)]     // exactly 100% - still Warning (not "Over")
    [InlineData(1_001, BudgetStatus.Over)]        // 100.1%
    [InlineData(2_000, BudgetStatus.Over)]        // 200%
    public async Task Handle_StatusThresholds(int spent, BudgetStatus expected)
    {
        Category category = NewCategory();
        Budget budget = NewBudget(category.Id, 1_000m);
        BudgetPeriod? period = spent == 0
            ? null
            : NewPeriod(budget.Id, 2026, 5, spent);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            budgets: [budget],
            budgetPeriods: period is null ? [] : [period]);

        var handler = new GetBudgetsQueryHandler(db, Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(
            new GetBudgetsQuery(2026, 5), CancellationToken.None);

        result.Value[0].Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_ComputesRemainingAndPopulatesCategoryName()
    {
        Category category = NewCategory("Groceries");
        Budget budget = NewBudget(category.Id, 1_000m);
        BudgetPeriod period = NewPeriod(budget.Id, 2026, 5, 300m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [category],
            budgets: [budget],
            budgetPeriods: [period]);

        var handler = new GetBudgetsQueryHandler(db, Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(
            new GetBudgetsQuery(2026, 5), CancellationToken.None);

        BudgetDto dto = result.Value[0];
        dto.MonthlyLimit.Should().Be(1_000m);
        dto.Spent.Should().Be(300m);
        dto.Remaining.Should().Be(700m);
        dto.CategoryName.Should().Be("Groceries");
        dto.CategoryId.Should().Be(category.Id);
    }
}
