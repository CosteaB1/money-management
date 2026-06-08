using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.Infrastructure.FxRates;
using MoneyManagement.Infrastructure.Tests.Database;

namespace MoneyManagement.Infrastructure.Tests.FxRates;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): pins the FX converter's rounding /
/// precision contract. The converter does NOT round to 2-dp money — it returns
/// full decimal precision and the caller rounds for display. These probe the
/// inverse-division path (which produces non-terminating quotients), the
/// identity short-circuit at odd values, and the inverse-of-inverse symmetry.
/// Runs against the real Npgsql provider on the throwaway inttest DB; uses
/// fictional currency codes and cleans up by id.
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class EfFxConverterRoundingTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context = IntegrationDbContextFactory.Create();
    private readonly List<Guid> _seeded = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_seeded.Count > 0)
        {
            await _context.FxRates.Where(r => _seeded.Contains(r.Id)).ExecuteDeleteAsync();
        }

        await _context.DisposeAsync();
    }

    private async Task SeedAsync(string from, string to, decimal rate, DateOnly asOf, FxRateSource source)
    {
        FxRate fx = FxRate.Create(from, to, rate, asOf, source).Value;
        _seeded.Add(fx.Id);
        _context.FxRates.Add(fx);
        await _context.SaveChangesAsync();
    }

    private EfFxConverter Converter() => new(_context);

    [Fact]
    public async Task Convert_InverseRate_NonTerminatingQuotient_ReturnsFullPrecision_NotRounded()
    {
        // Inverse pair only: XBB -> XBA at rate 3. Converting 100 XBA -> XBB is
        // 100 / 3 = 33.3333..., which is NOT a clean 2-dp money value. The
        // converter must NOT pre-round (that would lose precision the caller may
        // want); it returns the full-precision decimal.
        await SeedAsync("XBB", "XBA", 3m, new DateOnly(2026, 1, 1), FxRateSource.Manual);

        decimal? result = await Converter().ConvertAsync(100m, "XBA", "XBB", new DateOnly(2026, 6, 1), default);

        result.Should().NotBeNull();
        // 100 * (1/3). 1/3 in decimal is 0.3333333333333333333333333333 (28-29
        // sig digits), so the product is ~33.333... — definitely not 33.33.
        result!.Value.Should().BeApproximately(33.3333333m, 0.0001m);
        result.Value.Should().NotBe(33.33m);
    }

    [Fact]
    public async Task Convert_DirectRate_FractionalRate_KeepsSubCentPrecision()
    {
        // BNM per-unit rates carry 6 dp (numeric(18,6)). A rate like 0.113120
        // (JPY) times an odd amount produces sub-cent precision that must survive.
        await SeedAsync("XBC", "XBD", 0.113120m, new DateOnly(2026, 1, 1), FxRateSource.Manual);

        decimal? result = await Converter().ConvertAsync(777m, "XBC", "XBD", new DateOnly(2026, 6, 1), default);

        // 777 * 0.113120 = 87.89424 exactly.
        result.Should().Be(87.89424m);
    }

    [Fact]
    public async Task Convert_Identity_AtOddValue_ReturnsExactInput_EvenWithNonMatchingRowsPresent()
    {
        // from == to short-circuits before any lookup; verify the exact decimal
        // is preserved bit-for-bit at a value with trailing precision.
        decimal odd = 12345.678901m;
        decimal? result = await Converter().ConvertAsync(odd, "XBE", "XBE", new DateOnly(2026, 6, 1), default);

        result.Should().Be(odd);
    }

    [Fact]
    public async Task Convert_RoundTrip_DirectThenInverse_DoesNotRecoverExactly_ButIsWithinTolerance()
    {
        // Direct XBF->XBG at 7.5; inverse recovery XBG->XBF uses 1/7.5.
        await SeedAsync("XBF", "XBG", 7.5m, new DateOnly(2026, 1, 1), FxRateSource.Manual);

        decimal? forward = await Converter().ConvertAsync(40m, "XBF", "XBG", new DateOnly(2026, 6, 1), default);
        forward.Should().Be(300m);

        decimal? back = await Converter().ConvertAsync(forward!.Value, "XBG", "XBF", new DateOnly(2026, 6, 1), default);

        // EXPECTED, documented behavior — NOT a bug: the inverse path computes
        // `amount * (1m / rate)`, and `1m / 7.5m` is not exactly representable in
        // decimal, so the round-trip yields 39.999999999999999999999999990, not
        // 40. The converter deliberately does NOT round — callers round at the
        // 2-dp money display boundary. This test pins that "round-trips don't
        // recover the cent exactly" reality so it isn't mistaken for a defect.
        back.Should().NotBe(40m);
        back!.Value.Should().BeApproximately(40m, 0.000001m);
        Math.Round(back.Value, 2).Should().Be(40.00m);
    }
}
