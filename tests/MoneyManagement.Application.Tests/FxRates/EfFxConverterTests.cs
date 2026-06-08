using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Infrastructure.FxRates;

namespace MoneyManagement.Application.Tests.FxRates;

public class EfFxConverterTests
{
    private static readonly DateOnly AsOf = new(2026, 5, 1);

    private static IFxConverter Build(params (string From, string To, decimal Rate, DateOnly AsOf)[] rates)
    {
        FxRate[] entities = rates
            .Select(r => FxRate.Create(r.From, r.To, r.Rate, r.AsOf).Value)
            .ToArray();

        IApplicationDbContext db = FakeApplicationDbContext.Create(fxRates: entities);
        return new EfFxConverter(db);
    }

    private static IFxConverter BuildWithSources(
        params (string From, string To, decimal Rate, DateOnly AsOf, FxRateSource Source)[] rates)
    {
        FxRate[] entities = rates
            .Select(r => FxRate.Create(r.From, r.To, r.Rate, r.AsOf, r.Source).Value)
            .ToArray();

        IApplicationDbContext db = FakeApplicationDbContext.Create(fxRates: entities);
        return new EfFxConverter(db);
    }

    [Fact]
    public async Task ConvertAsync_WhenFromEqualsTo_ReturnsAmountUnchanged()
    {
        IFxConverter converter = Build();

        decimal? result = await converter.ConvertAsync(123.45m, "MDL", "MDL", AsOf, CancellationToken.None);

        result.Should().Be(123.45m);
    }

    [Fact]
    public async Task ConvertAsync_WithDirectRate_UsesIt()
    {
        IFxConverter converter = Build(("USD", "MDL", 17.50m, new DateOnly(2026, 1, 1)));

        decimal? result = await converter.ConvertAsync(10m, "USD", "MDL", AsOf, CancellationToken.None);

        result.Should().Be(175.00m);
    }

    [Fact]
    public async Task ConvertAsync_WithoutDirectButInverseRate_InvertsIt()
    {
        // MDL->USD = 0.05 means 1 MDL = 0.05 USD. We want USD->MDL, so 1/0.05.
        IFxConverter converter = Build(("MDL", "USD", 0.05m, new DateOnly(2026, 1, 1)));

        decimal? result = await converter.ConvertAsync(10m, "USD", "MDL", AsOf, CancellationToken.None);

        result.Should().Be(10m * (1m / 0.05m));
    }

    [Fact]
    public async Task ConvertAsync_WithNoRate_ReturnsNull()
    {
        IFxConverter converter = Build();

        decimal? result = await converter.ConvertAsync(10m, "USD", "MDL", AsOf, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ConvertAsync_WithMultipleHistoricalRates_PicksMostRecentOnOrBeforeAsOf()
    {
        IFxConverter converter = Build(
            ("USD", "MDL", 17.00m, new DateOnly(2025, 12, 1)),
            ("USD", "MDL", 17.50m, new DateOnly(2026, 1, 1)),
            ("USD", "MDL", 18.00m, new DateOnly(2026, 6, 1))); // future, must be ignored

        // asOf = 2026-05-01 -> latest on/before is 2026-01-01 @ 17.50
        decimal? result = await converter.ConvertAsync(1m, "USD", "MDL", AsOf, CancellationToken.None);

        result.Should().Be(17.50m);
    }

    [Fact]
    public async Task ConvertAsync_WhenOnlyFutureRateExists_ReturnsNull()
    {
        IFxConverter converter = Build(("USD", "MDL", 17.50m, new DateOnly(2027, 1, 1)));

        decimal? result = await converter.ConvertAsync(10m, "USD", "MDL", AsOf, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ConvertAsync_WhenManualAndBnmAutoCoexistForSameTriple_PrefersManual()
    {
        // Same asOf, same pair — Manual must win. The two rows are legal
        // together because the unique index now includes Source.
        IFxConverter converter = BuildWithSources(
            ("USD", "MDL", 17.00m, new DateOnly(2026, 5, 1), FxRateSource.BnmAuto),
            ("USD", "MDL", 18.00m, new DateOnly(2026, 5, 1), FxRateSource.Manual));

        decimal? result = await converter.ConvertAsync(1m, "USD", "MDL", AsOf, CancellationToken.None);

        result.Should().Be(18.00m);
    }
}
