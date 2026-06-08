using FluentAssertions;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Budgets;

public class BudgetTests
{
    private static readonly Guid CategoryId = Guid.CreateVersion7();

    private static Money ValidLimit(decimal amount = 1_500m) => new(amount, "MDL");

    [Fact]
    public void Create_WithValidInput_Succeeds()
    {
        Result<Budget> result = Budget.Create(CategoryId, ValidLimit(1_500m));

        result.IsSuccess.Should().BeTrue();
        Budget budget = result.Value;
        budget.CategoryId.Should().Be(CategoryId);
        budget.MonthlyLimit.Should().Be(ValidLimit(1_500m));
        budget.IsArchived.Should().BeFalse();
        budget.GetDomainEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("RON")]
    public void Create_WithNonMdlCurrency_ReturnsMdlOnly(string currency)
    {
        Result<Budget> result = Budget.Create(CategoryId, new Money(1_000m, currency));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.MdlOnly);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1_000)]
    public void Create_WithNonPositiveLimit_ReturnsLimitMustBePositive(decimal amount)
    {
        Result<Budget> result = Budget.Create(CategoryId, new Money(amount, "MDL"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.LimitMustBePositive);
    }

    [Fact]
    public void UpdateLimit_WithValidLimit_UpdatesValue()
    {
        Budget budget = Budget.Create(CategoryId, ValidLimit(1_000m)).Value;

        Result result = budget.UpdateLimit(ValidLimit(2_500m));

        result.IsSuccess.Should().BeTrue();
        budget.MonthlyLimit.Amount.Should().Be(2_500m);
    }

    [Fact]
    public void UpdateLimit_WithNonMdlCurrency_Fails()
    {
        Budget budget = Budget.Create(CategoryId, ValidLimit(1_000m)).Value;

        Result result = budget.UpdateLimit(new Money(2_000m, "USD"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.MdlOnly);
        budget.MonthlyLimit.Amount.Should().Be(1_000m); // unchanged
    }

    [Fact]
    public void UpdateLimit_WithZeroAmount_Fails()
    {
        Budget budget = Budget.Create(CategoryId, ValidLimit(1_000m)).Value;

        Result result = budget.UpdateLimit(new Money(0m, "MDL"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.LimitMustBePositive);
    }

    [Fact]
    public void Archive_SetsIsArchivedToTrue()
    {
        Budget budget = Budget.Create(CategoryId, ValidLimit()).Value;

        Result result = budget.Archive();

        result.IsSuccess.Should().BeTrue();
        budget.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_OnAlreadyArchivedBudget_IsIdempotent()
    {
        Budget budget = Budget.Create(CategoryId, ValidLimit()).Value;
        budget.Archive();

        Result result = budget.Archive();

        result.IsSuccess.Should().BeTrue();
        budget.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Create_WithEmptyCategoryId_ReturnsNotFound()
    {
        Result<Budget> result = Budget.Create(Guid.Empty, ValidLimit());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.NotFound(Guid.Empty));
    }
}
