using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.GetPools;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The <c>/pools</c> list: every pool folded against its account's derived
/// balance today, so the NAV printed here is the NAV the next write command
/// would strike.
/// </summary>
public sealed class GetPoolsQueryHandlerTests
{
    /// <summary>USD/MDL on 2026-09-06, the rate every figure in POOLED-CAPITAL.md is quoted at.</summary>
    private const decimal UsdMdl = 17.2682m;

    private static IFxConverter Mdl() =>
        FakeFxConverter.WithTable(new Dictionary<string, decimal> { [PoolHarness.Currency] = UsdMdl });

    private static Task<Result<IReadOnlyList<PoolDto>>> ListAsync(
        PoolHarness harness,
        bool includeArchived = false,
        IFxConverter? fx = null) =>
        new GetPoolsQueryHandler(harness.Db, fx ?? Mdl(), harness.Clock)
            .Handle(new GetPoolsQuery(includeArchived), CancellationToken.None);

    [Fact]
    public async Task Handle_ExcludesArchivedPoolsByDefault()
    {
        var harness = PoolHarness.Create(now: new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc));
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        (await ListAsync(harness)).Value.Should().ContainSingle();

        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        Result<IReadOnlyList<PoolDto>> result = await ListAsync(harness);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("an archived pool is a finished story, not a hidden one");
    }

    [Fact]
    public async Task Handle_IncludeArchived_ReturnsThem()
    {
        var harness = PoolHarness.Create(now: new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc));
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        Result<IReadOnlyList<PoolDto>> result = await ListAsync(harness, includeArchived: true);

        PoolDto dto = result.Value.Should().ContainSingle().Which;
        dto.IsArchived.Should().BeTrue();
        dto.Id.Should().Be(harness.PoolId);
        dto.TotalUnits.Should().Be(1_092m, "archiving does not retire the owner's own units");
    }

    [Fact]
    public async Task Handle_WorkedExampleAfterTheWithdrawal_ReportsNavOwnerFractionAndOutsideCapital()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughWithdrawalAsync();

        Result<IReadOnlyList<PoolDto>> result = await ListAsync(example.Harness);

        PoolDto dto = result.Value.Should().ContainSingle().Which;

        dto.AccountId.Should().Be(example.AccountId);
        dto.AccountName.Should().Be("Binance");
        dto.Currency.Should().Be(PoolHarness.Currency);
        dto.InceptionDate.Should().Be(PoolWorkedExample.Inception);
        dto.ParticipantCount.Should().Be(3);

        // Nothing is owed, so pool value IS the account balance.
        dto.AccountBalance.Should().Be(PoolWorkedExample.PoolValueAfterWithdrawal);
        dto.UnpaidDistributionCash.Should().Be(0m);
        dto.UnpaidDistributionCount.Should().Be(0);
        dto.PoolValue.Should().Be(PoolWorkedExample.PoolValueAfterWithdrawal);

        dto.TotalUnits.Should().Be(PoolWorkedExample.TotalUnitsAfterWithdrawal);
        dto.NavPerUnit.Should().Be(PoolWorkedExample.NavAfterWithdrawal, "a redemption at NAV leaves NAV unchanged");

        // Unit counts only — the same ratio PoolAccountOwnershipSource hands the
        // dashboard, routed through the same helper so the two cannot disagree.
        dto.OwnerFraction.Should().Be(
            PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterWithdrawal);
        Math.Round(dto.OwnerFraction, 12).Should().Be(0.265044961955m, "§6: 34.38% → 26.50%");

        // Σ non-owner stakes: Andrei's 1,000 units at 1.04 and Bogdan's 961.53…
        dto.OutsideCapital.Should().Be(2_040m);

        dto.MissingFxRate.Should().BeFalse();
        dto.PoolValueMdl.Should().Be(PoolWorkedExample.PoolValueAfterWithdrawal * UsdMdl);
        dto.OutsideCapitalMdl.Should().Be(2_040m * UsdMdl);
    }

    [Fact]
    public async Task Handle_UnconvertibleCurrency_ReportsNullMdlAndFlagsMissingFxRate()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughWithdrawalAsync();

        // The identity converter has no USD→MDL rate, which is the production
        // shape of "no FxRate row for today".
        Result<IReadOnlyList<PoolDto>> result =
            await ListAsync(example.Harness, fx: FakeFxConverter.Identity());

        PoolDto dto = result.Value.Should().ContainSingle().Which;

        // NEVER a silent zero and never an implicit 1:1 — the same contract as
        // AccountDto.BalanceMdl. A zero here would read as "the pool is empty".
        dto.PoolValueMdl.Should().BeNull();
        dto.OutsideCapitalMdl.Should().BeNull();
        dto.MissingFxRate.Should().BeTrue();

        // The native figures are unaffected: FX is a presentation concern.
        dto.PoolValue.Should().Be(PoolWorkedExample.PoolValueAfterWithdrawal);
        dto.OutsideCapital.Should().Be(2_040m);
        dto.NavPerUnit.Should().Be(PoolWorkedExample.NavAfterWithdrawal);
    }

    [Fact]
    public async Task Handle_ClosedButUnpaidDistributions_AreSummedAndCounted()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughCloseAsync();

        Result<IReadOnlyList<PoolDto>> result = await ListAsync(example.Harness);

        PoolDto dto = result.Value.Should().ContainSingle().Which;

        // The close wrote the +160.14 mark and retired units, but no cash leg —
        // the 157.69 is still physically sitting in Binance.
        dto.AccountBalance.Should().Be(PoolWorkedExample.ClosePoolValue);
        dto.UnpaidDistributionCash.Should().Be(PoolWorkedExample.TotalPayout);
        dto.UnpaidDistributionCount.Should().Be(2);

        // Subtracting it is what stops the friends being paid twice on the same
        // profit, every month.
        dto.PoolValue.Should().Be(PoolWorkedExample.PoolValueAfterClose);
        dto.PoolValue.Should().Be(dto.AccountBalance - dto.UnpaidDistributionCash);

        dto.TotalUnits.Should().Be(PoolWorkedExample.TotalUnitsAfterClose);
        dto.NavPerUnit.Should().Be(PoolWorkedExample.NavAtClose, "the close leaves NAV where it was");

        // Both friends are back at exactly their 1,000 base.
        dto.OutsideCapital.Should().Be(2_000m);

        List<PoolUnitEvent> distributions = await example.Db.PoolUnitEvents
            .Where(e => e.Kind == PoolUnitEventKind.Distribution)
            .ToListAsync();

        distributions.Should().OnlyContain(e => e.SettledOn == null && e.MovementTransactionId == null);
    }
}
