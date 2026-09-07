using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Dashboard.GetNetWorth;
using MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Dashboard;

/// <summary>
/// Outside capital held inside the user's own accounts — money that was never
/// the user's to begin with, so it is multiplied out of the balance instead of
/// subtracted from the total afterwards.
/// <para>
/// The claim seam (<see cref="ExternalClaim"/>) cannot express this. A claim is
/// <c>principal − Σ settlements</c>: monotonically non-increasing until someone
/// records a settlement. A pooled stake is <c>units × navPerUnit</c> and moves
/// with the account, UP as well as down, with no event to record. The
/// rising/falling pair below is the whole reason this second seam exists — every
/// pre-existing claim fixture in this repo is non-increasing, so nothing else
/// here would catch a regression that assumed the outside interest can only
/// shrink.
/// </para>
/// </summary>
public sealed class NetWorthOwnershipTests
{
    // months = 3 gives Mar 31, Apr 30, May 20 (today).
    private static readonly DateTime FixedNow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly LongAgo = new(2020, 1, 1);
    private static readonly decimal OneThird = 1m / 3m;

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Account NewAccount(decimal opening, string currency = "MDL", bool archived = false)
    {
        Result<Account> result = Account.Create(
            $"Acct {Guid.NewGuid():N}",
            AccountType.Cash,
            new Money(opening, currency),
            LongAgo,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        if (archived)
        {
            result.Value.Archive();
        }

        return result.Value;
    }

    private static Transaction Tx(Guid accountId, TransactionDirection direction, decimal amount, DateOnly on)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            on,
            direction,
            new Money(amount, "MDL"),
            "row",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Loan ReceivedLoan(decimal principal)
    {
        Result<Loan> result = Loan.Create(
            LoanDirection.Received,
            "Ion",
            new Money(principal, "MDL"),
            new DateOnly(2026, 1, 15),
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        result.Value.SetDisbursementTransaction(Guid.CreateVersion7());
        return result.Value;
    }

    private static GetNetWorthQueryHandler Card(
        IApplicationDbContext db,
        IEnumerable<IAccountOwnershipSource> ownership,
        IFxConverter? fx = null) =>
        new(db, fx ?? FakeFxConverter.Identity(), new LoanExternalClaimSource(db), ownership, Clock());

    private static GetNetWorthTrendQueryHandler Trend(
        IApplicationDbContext db,
        IEnumerable<IAccountOwnershipSource> ownership,
        IFxConverter? fx = null) =>
        new(db, fx ?? FakeFxConverter.Identity(), new LoanExternalClaimSource(db), ownership, Clock());

    // ---------------------------------------------------------------- card --

    [Fact]
    public async Task Card_NoOwnershipSourceRegistered_LeavesEverythingWhollyOwned()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [NewAccount(10_000m)]);

        Result<NetWorthDto> result = await Card(db, []).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(10_000m);
        result.Value.OutsideCapitalMdl.Should().Be(0m);
    }

    [Fact]
    public async Task Card_SourceThatReportsNothing_MatchesNoSourceAtAll()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [NewAccount(10_000m)]);

        Result<NetWorthDto> registered = await Card(db, [FakeAccountOwnershipSource.Empty()])
            .Handle(new GetNetWorthQuery(), CancellationToken.None);
        Result<NetWorthDto> none = await Card(db, [])
            .Handle(new GetNetWorthQuery(), CancellationToken.None);

        registered.Value.Should().Be(none.Value);
    }

    [Fact]
    public async Task Card_HalfOwnedAccount_SplitsTheBalanceInTwo()
    {
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        Result<NetWorthDto> result = await Card(db, [FakeAccountOwnershipSource.Flat(account.Id, 0.5m)])
            .Handle(new GetNetWorthQuery(), CancellationToken.None);

        NetWorthDto dto = result.Value;
        dto.GrossAssetsMdl.Should().Be(5_000m, "only the user's share is an asset of the user's");
        dto.OutsideCapitalMdl.Should().Be(5_000m, "the other half is somebody else's money, held not owned");
        dto.NetWorthMdl.Should().Be(
            5_000m,
            "outside capital sits OUTSIDE the identity — it was never added, so it is never subtracted");
        dto.NetWorthMdl.Should().Be(dto.GrossAssetsMdl - dto.ExternalLiabilitiesMdl + dto.ExternalAssetsMdl);
    }

    [Fact]
    public async Task Card_WhollyOutsideCapital_LeavesGrossAtZero()
    {
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        Result<NetWorthDto> result = await Card(db, [FakeAccountOwnershipSource.Flat(account.Id, 0m)])
            .Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(0m);
        result.Value.OutsideCapitalMdl.Should().Be(10_000m);
        result.Value.NetWorthMdl.Should().Be(0m);
    }

    [Fact]
    public async Task Card_OwnershipNeverScalesTheClaimLegs()
    {
        // Fractions apply to ACCOUNT BALANCES only. A loan the user took is owed
        // in full regardless of who else has money in the account it landed in.
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [ReceivedLoan(2_000m)]);

        Result<NetWorthDto> result = await Card(db, [FakeAccountOwnershipSource.Flat(account.Id, 0.5m)])
            .Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(5_000m);
        result.Value.ExternalLiabilitiesMdl.Should().Be(2_000m, "the debt is not halved");
        result.Value.NetWorthMdl.Should().Be(3_000m);
    }

    [Fact]
    public async Task Card_GrossPlusOutside_EqualsEveryConvertibleAccountToTheCent()
    {
        // Deliberately NOT "every account": the unconvertible one drops out of
        // BOTH legs and is reported through the counter instead. The archived one
        // never enters the dashboard at all.
        Account mdl = NewAccount(10_000m);
        Account eur = NewAccount(1_000m, "EUR");
        Account unconvertible = NewAccount(100m, "USD");
        Account archived = NewAccount(9_999m, archived: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [mdl, eur, unconvertible, archived]);

        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal> { ["EUR"] = 20m });

        // A repeating fraction on purpose — the two halves must still reconstitute
        // the whole, not drift by a rounding crumb.
        IAccountOwnershipSource ownership = FakeAccountOwnershipSource.With(
            new AccountOwnership(mdl.Id, [new OwnedFractionPoint(LongAgo, OneThird)]),
            new AccountOwnership(eur.Id, [new OwnedFractionPoint(LongAgo, 0.5m)]),
            new AccountOwnership(unconvertible.Id, [new OwnedFractionPoint(LongAgo, 0.5m)]),
            new AccountOwnership(archived.Id, [new OwnedFractionPoint(LongAgo, 0.5m)]));

        Result<NetWorthDto> result = await Card(db, [ownership], fx)
            .Handle(new GetNetWorthQuery(), CancellationToken.None);

        NetWorthDto dto = result.Value;
        decimal convertibleTotal = 10_000m + 1_000m * 20m;

        Math.Round(dto.GrossAssetsMdl + dto.OutsideCapitalMdl, 2).Should().Be(convertibleTotal);
        dto.AccountsMissingFxRate.Should().Be(1);
        Math.Round(dto.GrossAssetsMdl, 2).Should().Be(13_333.33m, "10,000/3 owned in MDL plus half of 20,000 MDL");
        Math.Round(dto.OutsideCapitalMdl, 2).Should().Be(16_666.67m);
    }

    [Fact]
    public async Task Card_FractionAppliesAfterFxConversion()
    {
        Account account = NewAccount(1_000m, "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal> { ["EUR"] = 20m });

        Result<NetWorthDto> result = await Card(db, [FakeAccountOwnershipSource.Flat(account.Id, 0.25m)], fx)
            .Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(5_000m);
        result.Value.OutsideCapitalMdl.Should().Be(15_000m);

        // One rate lookup for the whole balance — splitting after conversion is
        // what keeps the halves free.
        await fx.Received(1).ConvertAsync(
            Arg.Any<decimal>(), "EUR", ReportingCurrencies.Mdl, Today, Arg.Any<CancellationToken>());
    }

    // --------------------------------------------------------------- trend --

    [Fact]
    public async Task Trend_PointBeforeTheFirstOwnershipPoint_IsWhollyOwned()
    {
        // The Apr 30 point is the END-OF-DAY case: the fraction that takes effect
        // that day already applies to that day's balance.
        Account account = NewAccount(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        IAccountOwnershipSource ownership = FakeAccountOwnershipSource.With(new AccountOwnership(
            account.Id,
            [new OwnedFractionPoint(new DateOnly(2026, 4, 30), 0.5m)]));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Trend(db, [ownership])
            .Handle(new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(1_000m, "nobody else's money was in the account on Mar 31");
        result.Value[1].NetWorthMdl.Should().Be(500m, "a point dated exactly asOf applies on that day");
        result.Value[2].NetWorthMdl.Should().Be(500m);
    }

    [Fact]
    public async Task Trend_EachPointUsesItsOwnFractionNotTodays()
    {
        Account account = NewAccount(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        IAccountOwnershipSource ownership = FakeAccountOwnershipSource.With(new AccountOwnership(
            account.Id,
            [
                new OwnedFractionPoint(new DateOnly(2026, 4, 1), 0.75m),
                new OwnedFractionPoint(new DateOnly(2026, 5, 1), 0.5m),
            ]));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Trend(db, [ownership])
            .Handle(new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(1_000m, "Mar 31 predates every point");
        result.Value[1].NetWorthMdl.Should().Be(750m, "Apr 30 sees the Apr 1 fraction, not today's");
        result.Value[2].NetWorthMdl.Should().Be(500m);
    }

    [Fact]
    public async Task Trend_OutsideStake_RISES_WhenTheAccountRises()
    {
        // THE case the claim seam cannot model. The friends' stake is a
        // proportion, so it grows with the pool: 400 -> 800 -> 1,600 with not one
        // settlement, subscription or event in between. A claim would have been
        // pinned at its principal.
        Account account = NewAccount(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions:
            [
                Tx(account.Id, TransactionDirection.Income, 1_000m, new DateOnly(2026, 4, 5)),
                Tx(account.Id, TransactionDirection.Income, 2_000m, new DateOnly(2026, 5, 5)),
            ]);

        // 60% the user's, 40% the friends' — distinct numbers so the owned share
        // and the outside stake can't be confused for one another.
        Result<IReadOnlyList<NetWorthTrendPointDto>> result =
            await Trend(db, [FakeAccountOwnershipSource.Flat(account.Id, 0.6m)])
                .Handle(new GetNetWorthTrendQuery(3), CancellationToken.None);

        decimal[] balances = [1_000m, 2_000m, 4_000m];
        decimal[] expectedOwned = [600m, 1_200m, 2_400m];

        result.Value.Select(p => p.NetWorthMdl).Should().Equal(expectedOwned);

        decimal[] outsideStake = [.. result.Value.Select((p, i) => balances[i] - p.NetWorthMdl)];
        outsideStake.Should().Equal(new[] { 400m, 800m, 1_600m });
        outsideStake.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Trend_OutsideStake_FALLS_WhenTheAccountFalls()
    {
        Account account = NewAccount(4_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions:
            [
                Tx(account.Id, TransactionDirection.Expense, 2_000m, new DateOnly(2026, 4, 5)),
                Tx(account.Id, TransactionDirection.Expense, 1_000m, new DateOnly(2026, 5, 5)),
            ]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result =
            await Trend(db, [FakeAccountOwnershipSource.Flat(account.Id, 0.6m)])
                .Handle(new GetNetWorthTrendQuery(3), CancellationToken.None);

        decimal[] balances = [4_000m, 2_000m, 1_000m];
        decimal[] expectedOwned = [2_400m, 1_200m, 600m];

        result.Value.Select(p => p.NetWorthMdl).Should().Equal(expectedOwned);

        decimal[] outsideStake = [.. result.Value.Select((p, i) => balances[i] - p.NetWorthMdl)];
        outsideStake.Should().Equal(new[] { 1_600m, 800m, 400m });
        outsideStake.Should().BeInDescendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Trend_NoOwnershipSourceRegistered_IsTheOldSeries()
    {
        Account account = NewAccount(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Trend(db, [])
            .Handle(new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value.Should().OnlyContain(p => p.NetWorthMdl == 1_000m);
    }

    [Fact]
    public async Task Trend_OwnershipOfAnotherAccount_DoesNotLeak()
    {
        Account mine = NewAccount(1_000m);
        Account pooled = NewAccount(2_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [mine, pooled]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result =
            await Trend(db, [FakeAccountOwnershipSource.Flat(pooled.Id, 0.5m)])
                .Handle(new GetNetWorthTrendQuery(1), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(2_000m, "1,000 wholly owned + half of 2,000");
    }

    // ------------------------------------------------- the REAL pool source --
    //
    // Everything above drives the seam through FakeAccountOwnershipSource, which
    // proves the CONSUMERS behave. The three below wire the real
    // PoolAccountOwnershipSource into the real handlers, over state built by the
    // real write commands, so the producer and the consumer are proven to agree.

    /// <summary>USD/MDL on 2026-09-06 — the rate every figure in POOLED-CAPITAL.md is quoted at.</summary>
    private const decimal UsdMdl = 17.2682m;

    private static IFxConverter UsdTable(decimal rate = UsdMdl) =>
        FakeFxConverter.WithTable(new Dictionary<string, decimal> { [PoolHarness.Currency] = rate });

    private static GetNetWorthQueryHandler PooledCard(PoolHarness harness, IFxConverter fx) =>
        new(
            harness.Db,
            fx,
            new LoanExternalClaimSource(harness.Db),
            [new PoolAccountOwnershipSource(harness.Db)],
            harness.Clock);

    private static GetNetWorthTrendQueryHandler PooledTrend(
        PoolHarness harness,
        IFxConverter fx,
        bool wireTheSource = true) =>
        new(
            harness.Db,
            fx,
            new LoanExternalClaimSource(harness.Db),
            wireTheSource ? [new PoolAccountOwnershipSource(harness.Db)] : [],
            harness.Clock);

    private static void Succeeded<T>(Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

    [Fact]
    public async Task Settling_a_distribution_moves_net_worth_by_exactly_zero()
    {
        // POOLED-CAPITAL.md §6, closing sentence: "the payout moved net worth by
        // exactly zero — gross fell 157.69 and the liability fell 157.69. Paying
        // someone what you owe them makes you no poorer. That identity is worth a
        // unit test."
        //
        // It only holds because OwnershipCurve retires a distribution's units at
        // SettledOn rather than at the close. Retire them at the close and the
        // owner is credited with their share of 157.69 of somebody else's money,
        // and a month-end close welds that error onto that trend point forever.
        PoolWorkedExample example = await PoolWorkedExample.ThroughCloseAsync();

        GetNetWorthQueryHandler card = PooledCard(example.Harness, UsdTable());

        NetWorthDto afterClose = (await card.Handle(new GetNetWorthQuery(), CancellationToken.None)).Value;

        await example.SettleEveryPayoutAsync();

        NetWorthDto afterSettle = (await card.Handle(new GetNetWorthQuery(), CancellationToken.None)).Value;

        // The owner's share, to the cent, on both sides of the payment.
        Math.Round(afterClose.GrossAssetsMdl / UsdMdl, 2).Should().Be(778.12m);
        Math.Round(afterSettle.GrossAssetsMdl / UsdMdl, 2).Should().Be(778.12m);
        Math.Round(afterSettle.GrossAssetsMdl, 2).Should().Be(Math.Round(afterClose.GrossAssetsMdl, 2));

        // …while the account really did lose 157.69, all of it outside capital.
        Math.Round((afterClose.GrossAssetsMdl + afterClose.OutsideCapitalMdl) / UsdMdl, 2)
            .Should().Be(PoolWorkedExample.ClosePoolValue);
        Math.Round((afterSettle.GrossAssetsMdl + afterSettle.OutsideCapitalMdl) / UsdMdl, 2)
            .Should().Be(PoolWorkedExample.PoolValueAfterClose);

        Math.Round((afterClose.OutsideCapitalMdl - afterSettle.OutsideCapitalMdl) / UsdMdl, 2)
            .Should().Be(PoolWorkedExample.TotalPayout);

        // And the pooled account does not break the identity outside capital sits
        // outside of. Asserted on BOTH readings, because a fraction applied in the
        // wrong place would still satisfy it on one of them.
        foreach (NetWorthDto dto in new[] { afterClose, afterSettle })
        {
            dto.NetWorthMdl.Should().Be(
                dto.GrossAssetsMdl - dto.ExternalLiabilitiesMdl + dto.ExternalAssetsMdl);
            dto.AccountsMissingFxRate.Should().Be(0);
            dto.OutsideCapitalMdl.Should().BeGreaterThan(0m);
        }
    }

    [Fact]
    public async Task Trend_PooledStake_RISES_PointOverPoint_AsThePoolGains()
    {
        // The case NOTHING else in the repo covers. Every ExternalClaim fixture is
        // monotonically non-increasing — a claim is `principal − Σ settlements` —
        // so a regression that assumed the outside interest can only shrink would
        // pass the whole suite. A pooled stake is `units × navPerUnit`: it rises
        // when Binance rises, with no settlement, subscription or event to record.
        //
        // The converter is deliberately 1:1 so every figure below reads in the
        // pool's own USD.
        var harness = PoolHarness.Create(
            now: new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
            openingBalance: 1_000m);

        await harness.SeedPoolAsync(poolValueAtInception: 1_000m);

        harness.Advance(10);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_000m, 3_000m));

        // Three marks and nothing else: no capital event, no unit event, no
        // settlement. The friend's stake still climbs from 3,000 to 7,500.
        await MarkAsync(harness, new DateOnly(2026, 4, 30), 6_000m);
        await MarkAsync(harness, new DateOnly(2026, 5, 31), 8_000m);
        await MarkAsync(harness, new DateOnly(2026, 6, 15), 10_000m);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result =
            await PooledTrend(harness, UsdTable(1m)).Handle(new GetNetWorthTrendQuery(4), CancellationToken.None);

        Succeeded(result);

        // Mar 31, Apr 30, May 31, then today (Jun 15). The owner holds 1,000 of
        // the 4,000 units throughout, so their share is a flat quarter.
        decimal[] balances = [4_000m, 6_000m, 8_000m, 10_000m];
        result.Value.Select(p => p.NetWorthMdl).Should().Equal(1_000m, 1_500m, 2_000m, 2_500m);

        decimal[] outsideStake = [.. result.Value.Select((p, i) => balances[i] - p.NetWorthMdl)];

        outsideStake.Should().Equal(new[] { 3_000m, 4_500m, 6_000m, 7_500m });
        outsideStake.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Trend_ArchivedPool_StillShapesPastPoints()
    {
        // Archiving requires zero outside units, so the pool's LAST fraction is
        // 1.0 either way. What the projection's IgnoreQueryFilters protects is the
        // MIDDLE of the story — the months that really did have somebody else's
        // money in the account.
        var harness = PoolHarness.Create(
            now: new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
            openingBalance: 1_000m);

        await harness.SeedPoolAsync(poolValueAtInception: 1_000m);

        harness.Advance(10);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_000m, 3_000m));

        await MarkAsync(harness, new DateOnly(2026, 3, 25), 8_000m);

        // Andrei exits at NAV on May 10: 3,000 units at 2.00 = 6,000 out.
        harness.Advance(46);
        harness.Today.Should().Be(new DateOnly(2026, 5, 10));
        Succeeded(await harness.RedeemAsync(andrei, 8_000m, 6_000m));

        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        harness.Advance(36);
        harness.Today.Should().Be(new DateOnly(2026, 6, 15));

        IFxConverter fx = UsdTable(1m);

        Result<IReadOnlyList<NetWorthTrendPointDto>> withTheCurve =
            await PooledTrend(harness, fx).Handle(new GetNetWorthTrendQuery(4), CancellationToken.None);
        Result<IReadOnlyList<NetWorthTrendPointDto>> withNoSource =
            await PooledTrend(harness, fx, wireTheSource: false)
                .Handle(new GetNetWorthTrendQuery(4), CancellationToken.None);

        Succeeded(withTheCurve);
        Succeeded(withNoSource);

        // Mar 31 and Apr 30 really were three quarters somebody else's, months
        // after the pool was wound down and archived. Drop the IgnoreQueryFilters
        // and those two points silently gain 6,000 of the friend's money.
        withNoSource.Value.Select(p => p.NetWorthMdl).Should().Equal(8_000m, 8_000m, 2_000m, 2_000m);
        withTheCurve.Value.Select(p => p.NetWorthMdl).Should().Equal(2_000m, 2_000m, 2_000m, 2_000m);
    }

    /// <summary>
    /// Re-prices the pooled account through the real <c>AdjustBalance</c> handler
    /// on <paramref name="on"/>. A pooled account accepts an Adjustment dated
    /// TODAY and nothing else, so the clock is moved to the mark's date first.
    /// </summary>
    private static async Task MarkAsync(PoolHarness harness, DateOnly on, decimal typedBalance)
    {
        harness.Clock.Advance(on.DayNumber - harness.Today.DayNumber);
        harness.Today.Should().Be(on);

        Result<AdjustBalanceResult> result =
            await new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock).Handle(
                new AdjustBalanceCommand(harness.Account.Id, BalanceChangeKind.Adjustment, typedBalance, on, Notes: null),
                CancellationToken.None);

        Succeeded(result);
    }
}
