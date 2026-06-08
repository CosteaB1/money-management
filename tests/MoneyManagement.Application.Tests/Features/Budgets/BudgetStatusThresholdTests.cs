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

/// <summary>
/// Adversarial overnight sweep (2026-06-02): pins the budget status bucketing
/// at the EXACT 80% and 100% boundaries. Documented contract:
/// OnTrack &lt; 80% &lt;= Warning &lt;= 100% &lt; Over. So 80% is Warning (>=),
/// 100% is Warning (not Over — Over requires strictly &gt; 100%), and anything
/// above 100% is Over.
/// </summary>
public sealed class BudgetStatusThresholdTests
{
    private static IDateTimeProvider Clock() =>
        FixedClock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc));

    private static IDateTimeProvider FixedClock(DateTime now)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    private static async Task<BudgetStatus> StatusFor(decimal limit, decimal spent)
    {
        Category cat = Category.Create("Food", CategoryFlow.Expense).Value;
        Budget budget = Budget.Create(cat.Id, new Money(limit, "MDL")).Value;

        BudgetPeriod period = BudgetPeriod.Create(budget.Id, 2026, 5).Value;
        if (spent > 0m)
        {
            period.AddSpend(spent);
        }

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [cat],
            budgets: [budget],
            budgetPeriods: [period]);

        var handler = new GetBudgetsQueryHandler(db, Clock());
        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(
            new GetBudgetsQuery(2026, 5), CancellationToken.None);

        return result.Value.Single().Status;
    }

    [Fact]
    public async Task Status_JustBelow80Percent_IsOnTrack()
    {
        (await StatusFor(1000m, 799.99m)).Should().Be(BudgetStatus.OnTrack);
    }

    [Fact]
    public async Task Status_Exactly80Percent_IsWarning()
    {
        // ratio == 0.80, threshold is >= 0.80 → Warning.
        (await StatusFor(1000m, 800m)).Should().Be(BudgetStatus.Warning);
    }

    [Fact]
    public async Task Status_Exactly100Percent_IsWarning_NotOver()
    {
        // ratio == 1.00; Over requires strictly > 1.00, so exactly-at-limit is
        // still Warning. (Off-by-one boundary that's easy to get wrong.)
        (await StatusFor(1000m, 1000m)).Should().Be(BudgetStatus.Warning);
    }

    [Fact]
    public async Task Status_JustOver100Percent_IsOver()
    {
        (await StatusFor(1000m, 1000.01m)).Should().Be(BudgetStatus.Over);
    }

    [Fact]
    public async Task Status_ZeroSpent_IsOnTrack()
    {
        (await StatusFor(1000m, 0m)).Should().Be(BudgetStatus.OnTrack);
    }
}
