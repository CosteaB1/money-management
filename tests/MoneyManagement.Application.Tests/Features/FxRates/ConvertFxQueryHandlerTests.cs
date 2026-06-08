using FluentAssertions;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.FxRates.ConvertFx;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.FxRates;

public class ConvertFxQueryHandlerTests
{
    private static readonly DateOnly AsOf = new(2026, 5, 1);

    private static IFxConverter ConverterReturning(decimal? value)
    {
        IFxConverter fx = Substitute.For<IFxConverter>();
        fx.ConvertAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(value));
        return fx;
    }

    [Fact]
    public async Task Handle_IdentityConversion_ReturnsAmountWithRateOne()
    {
        // Identity must short-circuit to rate 1 without touching the converter.
        IFxConverter fx = Substitute.For<IFxConverter>();
        var handler = new ConvertFxQueryHandler(fx);

        Result<ConvertFxResult> result = await handler.Handle(
            new ConvertFxQuery("MDL", "MDL", AsOf, 250m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasRate.Should().BeTrue();
        result.Value.ConvertedAmount.Should().Be(250m);
        result.Value.Rate.Should().Be(1m);

        await fx.DidNotReceiveWithAnyArgs().ConvertAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_WithRate_ReturnsConvertedAmountAndDerivedRate()
    {
        // 17,163 MDL -> 1000 USD: derived rate is converted/amount.
        IFxConverter fx = ConverterReturning(1_000m);
        var handler = new ConvertFxQueryHandler(fx);

        Result<ConvertFxResult> result = await handler.Handle(
            new ConvertFxQuery("MDL", "USD", AsOf, 17_163m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasRate.Should().BeTrue();
        result.Value.ConvertedAmount.Should().Be(1_000m);
        result.Value.Rate.Should().Be(1_000m / 17_163m);
    }

    [Fact]
    public async Task Handle_WithZeroAmount_ReturnsNullRate()
    {
        IFxConverter fx = ConverterReturning(0m);
        var handler = new ConvertFxQueryHandler(fx);

        Result<ConvertFxResult> result = await handler.Handle(
            new ConvertFxQuery("MDL", "USD", AsOf, 0m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasRate.Should().BeTrue();
        result.Value.ConvertedAmount.Should().Be(0m);
        result.Value.Rate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenConverterReturnsNull_HasRateFalse()
    {
        IFxConverter fx = ConverterReturning(null);
        var handler = new ConvertFxQueryHandler(fx);

        Result<ConvertFxResult> result = await handler.Handle(
            new ConvertFxQuery("USD", "MDL", AsOf, 10m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasRate.Should().BeFalse();
        result.Value.ConvertedAmount.Should().BeNull();
        result.Value.Rate.Should().BeNull();
    }

    [Theory]
    [InlineData("us", "MDL")]
    [InlineData("USD", "mdl")]
    [InlineData("USDT", "MDL")]
    [InlineData("US1", "MDL")]
    public async Task Handle_InvalidCurrency_Fails(string from, string to)
    {
        IFxConverter fx = Substitute.For<IFxConverter>();
        var handler = new ConvertFxQueryHandler(fx);

        Result<ConvertFxResult> result = await handler.Handle(
            new ConvertFxQuery(from, to, AsOf, 10m),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("fx.invalid_currency");
    }
}
