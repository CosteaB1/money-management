using FluentAssertions;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Budgets;

public class BudgetPeriodTests
{
    private static readonly Guid BudgetId = Guid.CreateVersion7();

    [Fact]
    public void Create_WithValidInput_StartsAtZero()
    {
        Result<BudgetPeriod> result = BudgetPeriod.Create(BudgetId, 2026, 5);

        result.IsSuccess.Should().BeTrue();
        BudgetPeriod period = result.Value;
        period.BudgetId.Should().Be(BudgetId);
        period.Year.Should().Be(2026);
        period.Month.Should().Be(5);
        period.Spent.Amount.Should().Be(0m);
        period.Spent.Currency.Should().Be("MDL");
    }

    [Fact]
    public void Create_WithEmptyBudgetId_Fails()
    {
        Result<BudgetPeriod> result = BudgetPeriod.Create(Guid.Empty, 2026, 5);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetPeriodErrors.BudgetRequired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Create_WithInvalidMonth_Fails(int month)
    {
        Result<BudgetPeriod> result = BudgetPeriod.Create(BudgetId, 2026, month);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetPeriodErrors.InvalidMonth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidYear_Fails(int year)
    {
        Result<BudgetPeriod> result = BudgetPeriod.Create(BudgetId, year, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetPeriodErrors.InvalidYear);
    }

    [Fact]
    public void AddSpend_WithPositiveAmount_IncrementsSpent()
    {
        BudgetPeriod period = BudgetPeriod.Create(BudgetId, 2026, 5).Value;

        Result first = period.AddSpend(100.50m);
        Result second = period.AddSpend(49.50m);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        period.Spent.Amount.Should().Be(150m);
        period.Spent.Currency.Should().Be("MDL");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1_000)]
    public void AddSpend_WithNonPositiveAmount_Fails(decimal amount)
    {
        BudgetPeriod period = BudgetPeriod.Create(BudgetId, 2026, 5).Value;

        Result result = period.AddSpend(amount);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetPeriodErrors.SpendMustBePositive);
        period.Spent.Amount.Should().Be(0m);
    }

    [Fact]
    public void SubtractSpend_WithPositiveAmount_DecrementsSpent()
    {
        BudgetPeriod period = BudgetPeriod.Create(BudgetId, 2026, 5).Value;
        period.AddSpend(200m);

        Result result = period.SubtractSpend(75.50m);

        result.IsSuccess.Should().BeTrue();
        period.Spent.Amount.Should().Be(124.50m);
        period.Spent.Currency.Should().Be("MDL");
    }

    [Fact]
    public void SubtractSpend_WhenWouldGoNegative_ClampsAtZero()
    {
        BudgetPeriod period = BudgetPeriod.Create(BudgetId, 2026, 5).Value;
        period.AddSpend(50m);

        Result result = period.SubtractSpend(75m);

        result.IsSuccess.Should().BeTrue();
        period.Spent.Amount.Should().Be(0m);
        period.Spent.Currency.Should().Be("MDL");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1_000)]
    public void SubtractSpend_WithNonPositiveAmount_FailsWithSpendMustBePositive(decimal amount)
    {
        BudgetPeriod period = BudgetPeriod.Create(BudgetId, 2026, 5).Value;
        period.AddSpend(100m);

        Result result = period.SubtractSpend(amount);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetPeriodErrors.SpendMustBePositive);
        period.Spent.Amount.Should().Be(100m);
    }
}
