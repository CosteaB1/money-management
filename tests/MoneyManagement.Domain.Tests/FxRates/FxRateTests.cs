using FluentAssertions;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.FxRates;

public class FxRateTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);

    [Fact]
    public void Create_WithValidInput_ReturnsSuccess()
    {
        Result<FxRate> result = FxRate.Create("USD", "MDL", 17.50m, AsOf);

        result.IsSuccess.Should().BeTrue();
        FxRate rate = result.Value;
        rate.FromCurrency.Should().Be("USD");
        rate.ToCurrency.Should().Be("MDL");
        rate.Rate.Should().Be(17.50m);
        rate.AsOf.Should().Be(AsOf);
        rate.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("usd", "MDL")]
    [InlineData("USD", "mdl")]
    [InlineData("US", "MDL")]
    [InlineData("USDX", "MDL")]
    [InlineData("", "MDL")]
    [InlineData("USD", "")]
    public void Create_WithInvalidCurrencyFormat_ReturnsInvalidCurrency(string from, string to)
    {
        Result<FxRate> result = FxRate.Create(from, to, 1m, AsOf);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FxRateErrors.InvalidCurrency);
    }

    [Fact]
    public void Create_WithSameSourceAndTarget_ReturnsSameSourceAndTargetCurrency()
    {
        Result<FxRate> result = FxRate.Create("MDL", "MDL", 1m, AsOf);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FxRateErrors.SameSourceAndTargetCurrency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_WithNonPositiveRate_ReturnsRateMustBePositive(double rate)
    {
        Result<FxRate> result = FxRate.Create("USD", "MDL", (decimal)rate, AsOf);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FxRateErrors.RateMustBePositive);
    }

    [Fact]
    public void Create_WithoutSourceArgument_DefaultsToManual()
    {
        Result<FxRate> result = FxRate.Create("USD", "MDL", 17.50m, AsOf);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(FxRateSource.Manual);
    }

    [Fact]
    public void Create_WithBnmAutoSource_PersistsSource()
    {
        Result<FxRate> result = FxRate.Create("USD", "MDL", 17.50m, AsOf, FxRateSource.BnmAuto);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(FxRateSource.BnmAuto);
    }

    [Fact]
    public void UpdateRate_WithPositiveValue_OverwritesRate()
    {
        FxRate rate = FxRate.Create("USD", "MDL", 17.50m, AsOf, FxRateSource.BnmAuto).Value;

        Result update = rate.UpdateRate(18.25m);

        update.IsSuccess.Should().BeTrue();
        rate.Rate.Should().Be(18.25m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateRate_WithNonPositiveValue_FailsAndLeavesRateUntouched(double newRate)
    {
        FxRate rate = FxRate.Create("USD", "MDL", 17.50m, AsOf, FxRateSource.BnmAuto).Value;

        Result update = rate.UpdateRate((decimal)newRate);

        update.IsFailure.Should().BeTrue();
        update.Error.Should().Be(FxRateErrors.RateMustBePositive);
        rate.Rate.Should().Be(17.50m);
    }
}
