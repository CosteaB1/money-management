using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The producer behind the net-worth ownership seam: pools ⋈ participants ⋈ unit
/// events, folded into one dated owner-fraction step function per account.
/// <para>
/// Everything asserted here is a ratio of UNIT COUNTS. No balance, no NAV, no FX
/// — that is the contract that keeps the source at one flat query and makes a
/// NAV later found to be wrong misprice nothing retroactively.
/// </para>
/// </summary>
public sealed class PoolAccountOwnershipSourceTests
{
    private static Task<IReadOnlyList<AccountOwnership>> HistoryOf(IApplicationDbContext db) =>
        new PoolAccountOwnershipSource(db).GetHistoryAsync(CancellationToken.None);

    private static Account UsdAccount(string name, decimal opening) =>
        Account.Create(
            name,
            AccountType.CryptoExchange,
            new Money(opening, PoolHarness.Currency),
            new DateOnly(2026, 1, 1),
            notes: null).Value;

    [Fact]
    public async Task GetHistoryAsync_WorkedExample_EmitsTheOwnerFractionCurve()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughWithdrawalAsync();

        IReadOnlyList<AccountOwnership> history = await HistoryOf(example.Db);

        AccountOwnership ownership = history.Should().ContainSingle().Which;
        ownership.AccountId.Should().Be(example.AccountId);

        // One point per distinct date, oldest first, each carrying END-OF-DAY
        // state. The fractions are written as the divisions they actually are so
        // the numerator and the denominator stay legible: seed only, then
        // Andrei's 1,000, then Bogdan's 961.53…, then the owner's 400 out.
        ownership.Points.Should().Equal(
            new OwnedFractionPoint(PoolWorkedExample.Inception, 1m),
            new OwnedFractionPoint(
                PoolWorkedExample.AndreiSubscribes,
                PoolWorkedExample.SeedUnits / (PoolWorkedExample.SeedUnits + PoolWorkedExample.AndreiUnits)),
            new OwnedFractionPoint(
                PoolWorkedExample.BogdanSubscribes,
                PoolWorkedExample.SeedUnits
                    / (PoolWorkedExample.SeedUnits + PoolWorkedExample.AndreiUnits + PoolWorkedExample.BogdanUnits)),
            new OwnedFractionPoint(
                PoolWorkedExample.OwnerWithdraws,
                PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterWithdrawal));

        // §6's headline: the 400 to Bybit diluted the OWNER and nobody else.
        Math.Round(ownership.Points[0].Fraction, 4).Should().Be(1.0000m);
        Math.Round(ownership.Points[2].Fraction, 4).Should().Be(0.3576m);
        Math.Round(ownership.Points[3].Fraction, 4).Should().Be(0.2650m, "34.38% → 26.50%, per §2(iv)");
    }

    [Fact]
    public async Task GetHistoryAsync_NoPools_ReturnsEmpty()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [UsdAccount("Binance", 1_050m)]);
        var source = new PoolAccountOwnershipSource(db);

        (await source.GetHistoryAsync(CancellationToken.None)).Should().BeEmpty();

        // And an empty result has to be INDISTINGUISHABLE from no source at all.
        // The opposite default ("no record means owns nothing") would zero out
        // the whole dashboard for every app that has never created a pool.
        var anyAccount = Guid.CreateVersion7();
        var anyDate = new DateOnly(2026, 6, 30);

        AccountOwnershipLedger registered =
            await AccountOwnershipLedger.LoadAsync([source], CancellationToken.None);
        AccountOwnershipLedger unregistered =
            await AccountOwnershipLedger.LoadAsync([], CancellationToken.None);

        registered.OwnedFractionAsOf(anyAccount, anyDate).Should().Be(1m);
        unregistered.OwnedFractionAsOf(anyAccount, anyDate).Should().Be(1m);
    }

    [Fact]
    public async Task GetHistoryAsync_TwoPooledAccounts_KeepsTheCurvesSeparate()
    {
        Account second = UsdAccount("Bybit", 1_000m);
        PoolWorkedExample example = await PoolWorkedExample.ThroughWithdrawalAsync(second);

        // A second pool, on a second account, diluted three times as hard as the
        // first. One pool per account is enforced forever by an UNFILTERED unique
        // index, so grouping by account is the same partition as grouping by pool
        // — an account can never accumulate two curves that would have to be
        // multiplied together.
        Result<CreatePoolResponse> other = await example.Harness.CreatePoolAsync(
            accountId: second.Id,
            poolValueAtInception: 1_000m,
            ownerName: "Me",
            backfills: [new BackdatedSubscription("Vasile", example.Harness.Today, 3_000m, 1_000m)]);

        other.IsSuccess.Should().BeTrue(other.IsFailure ? other.Error.Code : null);

        IReadOnlyList<AccountOwnership> history = await HistoryOf(example.Db);
        history.Should().HaveCount(2);

        AccountOwnership pooled = history.Single(o => o.AccountId == example.AccountId);
        AccountOwnership sibling = history.Single(o => o.AccountId == second.Id);

        // Account A is byte-for-byte what it was before B existed.
        pooled.Points.Should().HaveCount(4);
        pooled.Points[^1].Should().Be(new OwnedFractionPoint(
            PoolWorkedExample.OwnerWithdraws,
            PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterWithdrawal));

        // …and B is a quarter owned, with the seed and the backfill collapsing
        // into the single end-of-day point they share a date with.
        sibling.Points.Should().Equal(
            new OwnedFractionPoint(PoolWorkedExample.OwnerWithdraws, 1_000m / 4_000m));
    }

    [Fact]
    public async Task GetHistoryAsync_TwoEventsOnOneDay_EmitOneEndOfDayPoint()
    {
        var harness = PoolHarness.Create(now: new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc));
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);

        // Both friends arrive on the same day. The fraction the trend multiplies
        // into that day's balance has to be the state after BOTH — the balance
        // ledger's own cutoff is `date <= asOf`, and a fraction that lagged by
        // one movement would inflate the owner's share for exactly one point.
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> first = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Code : null);

        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Result<RecordSubscriptionResponse> second = await harness.SubscribeAsync(bogdan, 2_092m, 1_000m);
        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Code : null);

        AccountOwnership ownership = (await HistoryOf(harness.Db)).Should().ContainSingle().Which;

        ownership.Points.Should().Equal(
            new OwnedFractionPoint(new DateOnly(2026, 6, 6), 1m),
            new OwnedFractionPoint(new DateOnly(2026, 6, 8), 1_092m / 3_092m));

        ownership.Points[^1].Fraction.Should().NotBe(
            1_092m / 2_092m,
            "the point carries the state after every movement effective that day, not after the first");
    }

    [Fact]
    public async Task GetHistoryAsync_ArchivedPool_StillEmitsItsCurve()
    {
        var harness = PoolHarness.Create(now: new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc));
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_092m, 1_000m)).IsSuccess.Should().BeTrue();

        harness.Advance(2);
        Result<RecordRedemptionResponse> exit = await harness.RedeemAsync(andrei, 2_092m, 1_000m);
        exit.IsSuccess.Should().BeTrue(exit.IsFailure ? exit.Error.Code : null);

        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();
        (await harness.Db.Pools.IgnoreQueryFilters().SingleAsync()).IsArchived.Should().BeTrue();

        // Archiving requires zero outside units, so the curve's LAST point is
        // 1.0 either way. What the projection's IgnoreQueryFilters protects is
        // the MIDDLE — the eight days that really did have somebody else's money
        // in the account. Filtering the pool out would silently rewrite them.
        AccountOwnership ownership = (await HistoryOf(harness.Db)).Should().ContainSingle().Which;

        ownership.Points.Should().Equal(
            new OwnedFractionPoint(new DateOnly(2026, 6, 6), 1m),
            new OwnedFractionPoint(new DateOnly(2026, 6, 8), 1_092m / 2_092m),
            new OwnedFractionPoint(new DateOnly(2026, 6, 10), 1m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 6, 9)).Should().Be(1_092m / 2_092m);
    }

    [Fact]
    public async Task GetHistoryAsync_ArchivedParticipant_KeepsTheirUnitsInTheDenominator()
    {
        var harness = PoolHarness.Create(now: new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc));
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_092m, 1_000m)).IsSuccess.Should().BeTrue();

        // Archived WHILE HOLDING UNITS. PoolParticipant.Archive refuses that, and
        // there is no archive-participant command at all, so this state is only
        // reachable through a restore or an out-of-band write — which is exactly
        // why the projection must not filter on it. Σ participantUnits ==
        // totalUnits is the model's core identity; dropping an archived row from
        // the denominator hands their stake to the owner with no event to explain
        // it.
        PoolParticipant row = await harness.Db.PoolParticipants.SingleAsync(p => p.Id == andrei);
        row.Archive(unitsHeld: 0m).IsSuccess.Should().BeTrue();
        row.IsArchived.Should().BeTrue();

        AccountOwnership ownership = (await HistoryOf(harness.Db)).Should().ContainSingle().Which;

        ownership.Points[^1].Fraction.Should().Be(
            1_092m / 2_092m,
            "an archived participant's units are still outstanding");
        ownership.Points[^1].Fraction.Should().NotBe(1m);
    }

    [Fact]
    public async Task GetHistoryAsync_ClosedButUnpaidDistribution_LeavesTheUnitsOutstanding()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughCloseAsync();

        IReadOnlyList<OwnedFractionPoint> afterClose =
            (await HistoryOf(example.Db)).Should().ContainSingle().Which.Points;

        // The close retired 143.35 units and recorded 157.69 as OWED. The cash is
        // still sitting in the account, so the curve must NOT move: the seam
        // multiplies this fraction into the account's RAW balance, and crediting
        // the owner with their share of money that is already somebody else's
        // would weld the error onto a month-end trend point permanently.
        afterClose.Should().HaveCount(4);
        afterClose[^1].Should().Be(new OwnedFractionPoint(
            PoolWorkedExample.OwnerWithdraws,
            PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterWithdrawal));

        afterClose.Should().NotContain(p => p.EffectiveFrom == PoolWorkedExample.MonthCloses);

        await example.SettleEveryPayoutAsync();

        IReadOnlyList<OwnedFractionPoint> afterSettle =
            (await HistoryOf(example.Db)).Should().ContainSingle().Which.Points;

        // Both lines settle on the same day, so they collapse into one point —
        // dated at the SETTLEMENT, not at the close.
        afterSettle.Should().HaveCount(5);
        afterSettle[^1].Should().Be(new OwnedFractionPoint(
            PoolWorkedExample.PayoutSettles,
            PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterClose));

        afterSettle[^1].Fraction.Should().BeGreaterThan(
            afterClose[^1].Fraction,
            "retiring the friends' units raises the owner's share by exactly enough to offset the cash leaving");
    }
}
