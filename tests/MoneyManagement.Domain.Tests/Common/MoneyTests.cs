using FluentAssertions;
using MoneyManagement.Domain.Common;

namespace MoneyManagement.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void Zero_ProducesZeroAmountInGivenCurrency()
    {
        var money = Money.Zero("USD");

        money.Amount.Should().Be(0m);
        money.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("MDL")]
    [InlineData("E")]
    public void IsValid_ForNonEmptyCurrencyUpToThreeChars_IsTrue(string currency)
    {
        new Money(10m, currency).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EURO")]
    public void IsValid_ForEmptyOrTooLongCurrency_IsFalse(string currency)
    {
        new Money(10m, currency).IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_ForNullCurrency_IsFalse()
    {
        new Money(10m, null!).IsValid.Should().BeFalse();
    }
}
