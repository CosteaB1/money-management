using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.Infrastructure.FxRates;
using MoneyManagement.Infrastructure.Tests.Database;

namespace MoneyManagement.Infrastructure.Tests.FxRates;

/// <summary>
/// <see cref="EfFxConverter"/> relies on relational ordering semantics
/// (<c>OrderByDescending(AsOf).ThenBy(Source == Manual ? 0 : 1)</c>) that an
/// in-memory provider can silently get wrong, so these run against the real
/// Npgsql provider on the
/// dedicated throwaway DB <c>money_management_inttest</c>. Each test uses
/// fictional, test-unique currency codes (never real ISO pairs in the app) and
/// deletes the rows it inserted, so the persistent inttest schema stays clean.
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class EfFxConverterTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context = IntegrationDbContextFactory.Create();
    private readonly List<Guid> _seeded = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Remove only the rows this test seeded, by id.
        if (_seeded.Count > 0)
        {
            await _context.FxRates
                .Where(r => _seeded.Contains(r.Id))
                .ExecuteDeleteAsync();
        }

        await _context.DisposeAsync();
    }

    private async Task SeedAsync(
        string from, string to, decimal rate, DateOnly asOf, FxRateSource source)
    {
        FxRate fx = FxRate.Create(from, to, rate, asOf, source).Value;
        _seeded.Add(fx.Id);
        _context.FxRates.Add(fx);
        await _context.SaveChangesAsync();
    }

    private EfFxConverter Converter() => new(_context);

    [Fact]
    public async Task Convert_SameCurrency_ReturnsAmount_NoDbHit()
    {
        // Identity short-circuits before any query; pass a context with no
        // matching rows to prove no lookup is needed.
        decimal? result = await Converter().ConvertAsync(123.45m, "XAA", "XAA", new DateOnly(2026, 1, 1), default);

        result.Should().Be(123.45m);
    }

    [Fact]
    public async Task Convert_DirectRate_MostRecentOnOrBeforeAsOf()
    {
        await SeedAsync("XAB", "XAC", 10m, new DateOnly(2026, 1, 1), FxRateSource.Manual);
        await SeedAsync("XAB", "XAC", 20m, new DateOnly(2026, 3, 1), FxRateSource.Manual);
        await SeedAsync("XAB", "XAC", 99m, new DateOnly(2026, 6, 1), FxRateSource.Manual); // after asOf

        decimal? result = await Converter().ConvertAsync(5m, "XAB", "XAC", new DateOnly(2026, 4, 1), default);

        // Picks the 2026-03-01 rate (most recent ≤ asOf), not the future one.
        result.Should().Be(100m);
    }

    [Fact]
    public async Task Convert_NoRateOnOrBeforeAsOf_ReturnsNull()
    {
        await SeedAsync("XAD", "XAE", 10m, new DateOnly(2026, 6, 1), FxRateSource.Manual);

        decimal? result = await Converter().ConvertAsync(5m, "XAD", "XAE", new DateOnly(2026, 1, 1), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Convert_NoUsableRate_ReturnsNull()
    {
        decimal? result = await Converter().ConvertAsync(5m, "XAF", "XAG", new DateOnly(2026, 1, 1), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Convert_InverseRate_UsedWhenNoDirect()
    {
        // Only the inverse pair (XAI -> XAH) exists; converter divides.
        await SeedAsync("XAI", "XAH", 4m, new DateOnly(2026, 1, 1), FxRateSource.Manual);

        decimal? result = await Converter().ConvertAsync(8m, "XAH", "XAI", new DateOnly(2026, 6, 1), default);

        // 8 / 4 = 2.
        result.Should().Be(2m);
    }

    [Fact]
    public async Task Convert_DirectPreferredOverInverse()
    {
        await SeedAsync("XAJ", "XAK", 10m, new DateOnly(2026, 1, 1), FxRateSource.Manual); // direct
        await SeedAsync("XAK", "XAJ", 99m, new DateOnly(2026, 1, 1), FxRateSource.Manual); // inverse

        decimal? result = await Converter().ConvertAsync(2m, "XAJ", "XAK", new DateOnly(2026, 6, 1), default);

        // Direct multiply (2 * 10), not inverse divide.
        result.Should().Be(20m);
    }

    [Fact]
    public async Task Convert_OnSameTriple_PrefersManualRate()
    {
        // On a same (from, to, asOf) triple, the Manual rate must win over the
        // BnmAuto rate. This runs against the real Npgsql provider, which is the
        // only way to catch the string-mapping pitfall: FxRate.Source is mapped
        // with HasConversion<string>(), so `ThenBy(r => r.Source)` would translate
        // to `ORDER BY source` on the string column where 'BnmAuto' < 'Manual'
        // lexicographically — returning BnmAuto first, the opposite of the
        // contract. The converter avoids this with an explicit numeric key
        // (`ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)`), which EF
        // renders as a CASE expression ordering Manual ahead of BnmAuto.
        await SeedAsync("XAL", "XAM", 17m, new DateOnly(2026, 2, 1), FxRateSource.BnmAuto);
        await SeedAsync("XAL", "XAM", 99m, new DateOnly(2026, 2, 1), FxRateSource.Manual);

        decimal? result = await Converter().ConvertAsync(1m, "XAL", "XAM", new DateOnly(2026, 6, 1), default);

        // Manual rate (99) wins, not the BnmAuto rate (17).
        result.Should().Be(99m);
    }
}
