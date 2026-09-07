using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Accounts.GetAccountDetail;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Accounts;

/// <summary>
/// The Performance card's owner-only arithmetic on a POOLED account, replayed
/// against POOLED-CAPITAL.md's worked example.
/// <para>
/// Left gross the three figures would not be approximate, they would be false:
/// the friends' USD 2,000 of subscriptions would read as the user's
/// contributions, their payouts as the user's withdrawals, and the whole pool's
/// return as the user's profit. The two corrections are deliberately DIFFERENT
/// operations, and each test below pins one of them:
/// </para>
/// <list type="number">
/// <item>a transfer leg is 0% or 100% the user's — included or excluded whole, never scaled;</item>
/// <item>a re-pricing mark IS scaled, by the fraction in force BEFORE that date's unit events.</item>
/// </list>
/// <para>
/// <b>The calendar is shifted from September to June 2026, and nothing else
/// is.</b> <c>Transaction.Create</c> judges its no-future-dates rule against the
/// real <c>DateTime.UtcNow</c> rather than the injected clock, so a literal
/// replay of a September that has not happened yet cannot be written at all.
/// Day offsets, amounts, typed pool values and every expected figure are the
/// document's.
/// </para>
/// <para>
/// <b>Why this suite replays §6 itself</b> rather than using the shared
/// <c>PoolWorkedExample</c> fixture: that one models the account as an opening
/// anchor of 1,050.00 with no transactions behind it, which is all the pool
/// arithmetic needs. This card is about the account's ACTIVITY, so it needs the
/// real shape - an anchor of 0.00 plus the pre-pool history that adds up to
/// 1,050.00 - or the headline figure (contributions 842.89, not 2,842.89) has
/// nothing to be measured against. Same calendar, same amounts, same oracle.
/// </para>
/// </summary>
public class AccountDetailOwnerArithmeticTests
{
    /// <summary>2026-06-06 stands in for the document's 2026-09-06.</summary>
    private static readonly DateTime Inception = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The Binanance account's real history before the pool, compressed from 12
    /// rows to the three the card actually buckets: contributions 842.89,
    /// withdrawals 800.00, net P&amp;L 1,007.11 — a derived balance of exactly
    /// 1,050.00 against an opening anchor of 0.00, which is what the document's
    /// figures start from. <b>Every one of these is the owner's alone</b>: they
    /// predate the pool, so nothing may touch them.
    /// </summary>
    private static void AddPrePoolHistory(IApplicationDbContext db, Guid accountId)
    {
        var date = new DateOnly(2026, 1, 5);

        db.Transactions.Add(Row(accountId, date, TransactionDirection.Income, 842.89m, isTransfer: true));
        db.Transactions.Add(Row(accountId, date, TransactionDirection.Expense, 800m, isTransfer: true));
        db.Transactions.Add(Row(accountId, date, TransactionDirection.Income, 1_007.11m, isAdjustment: true));
    }

    private static Transaction Row(
        Guid accountId,
        DateOnly date,
        TransactionDirection direction,
        decimal amount,
        bool isTransfer = false,
        bool isAdjustment = false,
        string currency = PoolHarness.Currency)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date,
            direction,
            new Money(amount, currency),
            "row",
            TransactionSource.Manual,
            isTransfer: isTransfer,
            counterAccountId: isTransfer ? Guid.CreateVersion7() : null,
            isAdjustment: isAdjustment);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>
    /// USD converts to MDL at exactly 1, so every <c>...Mdl</c> figure below can
    /// be read as the document's USD amount. The FX path itself is covered by
    /// <c>GetAccountDetailQueryHandlerTests</c>; mixing a real rate in here would
    /// only obscure the split being tested.
    /// </summary>
    private static IFxConverter UsdAtPar() =>
        FakeFxConverter.WithTable(new Dictionary<string, decimal> { [PoolHarness.Currency] = 1m });

    private static IDateTimeProvider FixedClock(DateTime utcNow)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

    private static Account Bybit()
    {
        Result<Account> result = Account.Create(
            "Bybit",
            AccountType.CryptoExchange,
            new Money(0m, PoolHarness.Currency),
            new DateOnly(2026, 1, 1),
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static async Task<AccountDetailDto> DetailAsync(
        PoolHarness harness,
        IEnumerable<IAccountOwnershipSource> sources)
    {
        var handler = new GetAccountDetailQueryHandler(harness.Db, UsdAtPar(), sources, harness.Clock);

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(harness.Account.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>
    /// The document's September, day by day, through the real write handlers.
    /// Leaves the clock on the settle date (its 2026-10-01), which is where the
    /// card is read.
    /// </summary>
    private static async Task<PoolHarness> ReplayTheWorkedExampleAsync()
    {
        Account bybit = Bybit();

        // Opening anchor 0.00 — the pre-pool rows below are what make the
        // balance 1,050.00, exactly as on the real account.
        var harness = PoolHarness.Create(now: Inception, openingBalance: 0m, extraAccounts: bybit);
        AddPrePoolHistory(harness.Db, harness.Account.Id);

        // Jun 6 (the doc's Sep 6). Binance really holds 1,092.00 against a
        // derived 1,050.00: the catch-up mark of +42.00 is written first, then
        // 1,092 units are seeded to the owner at a NAV of exactly 1.
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        // Jun 8 — Andrei subscribes 1,000 at the value the app already derives,
        // so no mark is written and NAV stays at 1.0000000000.
        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andreiIn =
            await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        andreiIn.IsSuccess.Should().BeTrue(andreiIn.IsFailure ? andreiIn.Error.Code : null);
        andreiIn.Value.MarkDelta.Should().Be(0m);

        // Jun 12 — Bogdan subscribes 1,000 against a real 2,175.68. The +83.68
        // mark goes first, THEN his units are priced at 1.04. That ordering is
        // what makes the pre-money fraction the right one for the mark.
        harness.Advance(4);
        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Result<RecordSubscriptionResponse> bogdanIn =
            await harness.SubscribeAsync(bogdan, 2_175.68m, 1_000m);
        bogdanIn.IsSuccess.Should().BeTrue(bogdanIn.IsFailure ? bogdanIn.Error.Code : null);
        bogdanIn.Value.MarkDelta.Should().Be(83.68m);
        bogdanIn.Value.NavPerUnit.Should().Be(1.04m);

        // Jun 18 — the OWNER moves 400 of their own money out to Bybit. Value at
        // the pool is unchanged from the derived total, so no mark; the two-leg
        // transfer is entirely the user's own withdrawal.
        harness.Advance(6);
        Result<RecordRedemptionResponse> toBybit =
            await harness.RedeemAsync(harness.OwnerId, 3_175.68m, 400m, destinationAccountId: bybit.Id);
        toBybit.IsSuccess.Should().BeTrue(toBybit.IsFailure ? toBybit.Error.Code : null);
        toBybit.Value.MarkDelta.Should().Be(0m);

        // Jun 30 — close the month at 2,935.82: mark +160.14, then both friends'
        // units retire at the new NAV. No cash moves yet.
        harness.Advance(12);
        Result<CloseDistributionResponse> close = await harness.CloseAsync(2_935.82m);
        close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.Code : null);
        close.Value.MarkDelta.Should().Be(160.14m);
        close.Value.Lines.Select(l => l.Cash).Should().BeEquivalentTo(new[] { 100m, 57.69m });

        // Jul 1 — and only now does the USDT actually leave, which is when the
        // payout legs exist to be excluded at all.
        harness.Advance(1);
        foreach (DistributionLine line in close.Value.Lines)
        {
            Result<SettleDistributionResponse> settled =
                await harness.SettleAsync(line.EventId);
            settled.IsSuccess.Should().BeTrue(settled.IsFailure ? settled.Error.Code : null);
        }

        return harness;
    }

    [Fact]
    public async Task Worked_example_reports_the_owners_contributions_withdrawals_and_pnl()
    {
        PoolHarness harness = await ReplayTheWorkedExampleAsync();

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        detail.IsPooled.Should().BeTrue();

        // The BALANCE stays gross — the account really does hold the friends'
        // money, and that divergence was decided explicitly. 2,935.82 marked at
        // the close, minus the 157.69 that has now been paid out.
        detail.Balance.Should().Be(2_778.13m);

        // CONTRIBUTIONS. Gross this is 2,842.89: the pre-pool 842.89 plus both
        // friends' 1,000. Their subscription legs are 0% the user's, so they
        // leave whole — amount AND count.
        detail.AllTime.ContributionsMdl.Should().Be(842.89m);
        detail.AllTime.ContributionCount.Should().Be(1);

        // WITHDRAWALS. Gross this is 1,357.69. The 400 moved to Bybit is the
        // owner's own money leaving and STAYS; the 100.00 and 57.69 payouts are
        // the friends' and go.
        detail.AllTime.WithdrawalsMdl.Should().Be(1_200m);
        detail.AllTime.WithdrawalCount.Should().Be(2);

        // NET P&L. Gross this is 1,292.93 — the whole pool's return presented as
        // the user's. Owner-only:
        //   1,007.11  pre-pool, fraction 1.0 (no pool existed yet)
        //     +42.00  the catch-up mark on inception day, struck before the seed
        //     +43.68  83.68 x 1092/2092, the fraction BEFORE Bogdan's 1,000
        //     +42.44  160.14 x 707.3846.../2668.9230..., the fraction before the close
        detail.AllTime.NetPnLMdl.Should().BeApproximately(1_135.23m, 0.005m);
        detail.AllTime.AdjustmentCount.Should().Be(4);

        detail.AllTime.MissingFxRate.Should().BeFalse();

        // Every row in this replay falls inside 2026, so the YTD window must
        // report the identical owner-only figures — the second window is not a
        // second code path where the split can be forgotten.
        detail.YearToDate.Should().Be(detail.AllTime);
    }

    [Fact]
    public async Task Owner_only_figures_reconcile_to_the_owners_actual_stake()
    {
        PoolHarness harness = await ReplayTheWorkedExampleAsync();

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        // THE PROOF, and the reason the pre-money rule is not a matter of taste.
        // The account opened at an anchor of 0.00 and the owner is the only
        // participant whose money the card is now describing, so
        //     contributions - withdrawals + P&L
        // has to reproduce what the owner actually holds. It does, to the cent:
        // 842.89 - 1,200.00 + 1,135.23 = 778.12 against a stake of 778.13.
        //
        // Use the END-OF-DAY fraction for the marks instead and this identity
        // misses by 13.76 - the owner would be credited with 29.92 of the Jun-12
        // mark instead of 43.68, i.e. with a slice of a gain earned entirely on
        // money that was in the pool before Bogdan's arrived.
        // 778.12, where the document's post-close table says 778.13: it took
        // the residual (2,778.13 - 1,000 - 1,000) while the register prices
        // units x NAV with NAV rounded to 12dp. A cent of rounding, on the same
        // number - and the card lands on the register's side of it, which is the
        // side the dashboard is on too.
        PoolSnapshot snapshot = await harness.SnapshotAsync();
        decimal ownerStake = snapshot.Find(harness.OwnerId)!.Stake!.Value;
        ownerStake.Should().Be(778.12m);

        decimal fromTheCard =
            detail.AllTime.ContributionsMdl - detail.AllTime.WithdrawalsMdl + detail.AllTime.NetPnLMdl;

        fromTheCard.Should().BeApproximately(ownerStake, 0.01m);
    }

    [Fact]
    public async Task Mark_is_split_by_the_fraction_in_force_before_that_days_units()
    {
        // The pre-money rule in isolation: ONE mark, and the pool arranged so the
        // two candidate fractions give visibly different answers.
        var harness = PoolHarness.Create(now: Inception, openingBalance: 1_092m);

        // Seeded at the value the app already derives, so no catch-up mark is
        // written and the only adjustment on the account is the one below.
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        await harness.SubscribeAsync(andrei, 1_092m, 1_000m);

        // The mark: +83.68 struck the instant before Bogdan's 1,000 is priced in.
        harness.Advance(4);
        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        await harness.SubscribeAsync(bogdan, 2_175.68m, 1_000m);

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        detail.AllTime.AdjustmentCount.Should().Be(1);

        // PRE-MONEY: 83.68 x 1092/2092. Bogdan gets none of this gain, correctly
        // — his money was not in the pool while it was earned.
        detail.AllTime.NetPnLMdl.Should().BeApproximately(43.68m, 0.000001m);

        // END-OF-DAY (83.68 x 1092/3053.5384615385) would be 29.92. The two
        // differ by more than 13, so this assertion cannot pass by accident.
        detail.AllTime.NetPnLMdl.Should().NotBeApproximately(29.92m, 1m);
    }

    [Fact]
    public async Task Archived_pool_still_hides_the_friends_legs_from_the_all_time_figures()
    {
        // Archiving requires zero outside units, so the account is wholly the
        // user's again TODAY — but the friend's money really did arrive and
        // leave, and an ALL-TIME figure still has to disown both legs. Pool
        // carries HasQueryFilter(!IsArchived), so this passes only because the
        // leg lookup ignores it.
        var harness = PoolHarness.Create(now: Inception, openingBalance: 1_092m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        await harness.SubscribeAsync(andrei, 1_092m, 1_000m);

        // Andrei takes his whole stake back out at the same NAV, then the pool
        // is wound up.
        harness.Advance(2);
        Result<RecordRedemptionResponse> exit = await harness.RedeemAsync(andrei, 2_092m, 1_000m);
        exit.IsSuccess.Should().BeTrue(exit.IsFailure ? exit.Error.Code : null);

        Result archived = await harness.ArchivePoolAsync();
        archived.IsSuccess.Should().BeTrue(archived.IsFailure ? archived.Error.Code : null);

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        // The badge is off — no live pool, so the guards have let go of the
        // account and so has the badge.
        detail.IsPooled.Should().BeFalse();

        // But the history is still not the user's. Gross both figures are 1,000.
        detail.AllTime.ContributionsMdl.Should().Be(0m);
        detail.AllTime.ContributionCount.Should().Be(0);
        detail.AllTime.WithdrawalsMdl.Should().Be(0m);
        detail.AllTime.WithdrawalCount.Should().Be(0);

        // And the account is back to holding only the owner's money.
        detail.Balance.Should().Be(1_092m);
    }

    // ---- the same-day marks (the 2026-09-07 defect) ----------------------

    /// <summary>
    /// A hand-typed snapshot through the real handler, dated today — the one
    /// mutating row <c>AdjustBalance</c> still permits on a pooled account.
    /// </summary>
    private static Task<Result<AdjustBalanceResult>> SnapshotAsync(PoolHarness harness, decimal balanceNow) =>
        new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock).Handle(
            new AdjustBalanceCommand(
                harness.Account.Id,
                BalanceChangeKind.Adjustment,
                balanceNow,
                harness.Today,
                Notes: null),
            CancellationToken.None);

    /// <summary>
    /// Puts a real millisecond between two commands, so the rows they write sort
    /// by id in the order the commands actually ran.
    /// <para>
    /// The whole slice orders same-day rows by their UUIDv7 id, and that is
    /// sound in production for exactly the reason stated in
    /// <c>PoolMarkLedger</c>: rows written by SEPARATE commands are separated by
    /// human interaction time. A unit test is not — it runs three pool commands
    /// inside one millisecond, and .NET's <c>Guid.CreateVersion7</c> carries no
    /// intra-millisecond counter — its low bits are random — so their ids come
    /// out in a random order (measured on .NET 10: 171 of 200 three-id runs came
    /// out unsorted). This helper restores the production condition rather than
    /// pretending the assumption isn't there.
    /// </para>
    /// <para>
    /// Only needed where a test puts two COMMANDS on one calendar day. The
    /// pairing of a mark to its own event never uses ids at all — see
    /// <c>PoolMarkAttribution</c> — so nothing below depends on this for the
    /// thing actually under test.
    /// </para>
    /// </summary>
    private static Task SeparateWritesAsync() => Task.Delay(2);

    [Fact]
    public async Task Mark_written_after_a_same_day_capital_event_takes_the_post_event_fraction()
    {
        // THE REGRESSION, reproduced exactly as it was seen live on 2026-09-07:
        // a pool bootstrapped, subscribed to AND closed on one calendar day.
        var harness = PoolHarness.Create(now: Inception, openingBalance: 0m);

        // Derived 0.00 against a real 1,092.00, so mark M0 = +1,092.00 is written
        // first and 1,092 units are then seeded to the owner at a NAV of 1.
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        await SeparateWritesAsync();

        // Still the same day. The app already agrees with 1,092, so Andrei's
        // 1,000 arrives with NO mark of its own. Owner 1,092 of 2,092 units.
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andreiIn = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        andreiIn.IsSuccess.Should().BeTrue(andreiIn.IsFailure ? andreiIn.Error.Code : null);
        andreiIn.Value.MarkDelta.Should().Be(0m);

        await SeparateWritesAsync();

        // And still the same day: the month closes against a real 2,300, which
        // writes mark M1 = +208.00 and then retires Andrei's profit at the new
        // NAV. Nothing here bypasses a guard — this is the shipped write slice.
        Result<CloseDistributionResponse> close = await harness.CloseAsync(2_300m);
        close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.Code : null);
        close.Value.MarkDelta.Should().Be(208m);

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        detail.AllTime.AdjustmentCount.Should().Be(2);
        detail.Balance.Should().Be(2_300m, "the balance stays gross — the account really holds all of it");

        // M0 was struck before any outside money existed at all, so it is the
        // owner's whole: 1,092.00 x 1.0.
        //
        // M1 was struck AFTER Andrei's 1,000 landed, so it splits
        // 1092/2092 = 0.521989 and the owner's share is 108.57 — not 208.00.
        (detail.AllTime.NetPnLMdl - 1_092m).Should().BeApproximately(108.5736m, 0.0001m);
        detail.AllTime.NetPnLMdl.Should().BeApproximately(1_200.5736m, 0.0001m);

        // What shipped: OwnedFractionAsOf(date - 1) read BOTH marks at the
        // pre-subscription 1.0 and reported 1,300.00 — the owner's Net P&L
        // over-stated by 99.43 USD (MDL 1,712.34 at 17.2222).
        detail.AllTime.NetPnLMdl.Should().NotBeApproximately(1_300m, 1m);
    }

    [Fact]
    public async Task Mark_on_a_day_with_no_capital_event_keeps_the_standing_fraction()
    {
        var harness = PoolHarness.Create(now: Inception, openingBalance: 1_092m);

        // Seeded and subscribed at values the app already derives, so neither
        // command writes a mark: the snapshot below is the account's only one.
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andreiIn = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        andreiIn.IsSuccess.Should().BeTrue(andreiIn.IsFailure ? andreiIn.Error.Code : null);
        andreiIn.Value.MarkDelta.Should().Be(0m);

        // Two days on, a snapshot on a day with NO unit event whatsoever — how a
        // month whose P&L pays nothing gets recorded, and much the commonest
        // shape of mark there is.
        harness.Advance(2);
        Result<AdjustBalanceResult> snapshot = await SnapshotAsync(harness, 2_200m);
        snapshot.IsSuccess.Should().BeTrue(snapshot.IsFailure ? snapshot.Error.Code : null);
        snapshot.Value.Delta.Should().Be(108m);

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        detail.AllTime.AdjustmentCount.Should().Be(1);

        // 108.00 x 1092/2092 — bit for bit what the old date-1 rule produced,
        // because on a day with no unit event the day before and the day itself
        // carry the same fraction. That equality is the whole reason nothing
        // outside the same-day case moved.
        detail.AllTime.NetPnLMdl.Should().BeApproximately(56.3748m, 0.0001m);
    }

    [Fact]
    public async Task Manual_snapshot_after_a_same_day_subscription_takes_the_post_subscription_fraction()
    {
        var harness = PoolHarness.Create(now: Inception, openingBalance: 1_092m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andreiIn = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        andreiIn.IsSuccess.Should().BeTrue(andreiIn.IsFailure ? andreiIn.Error.Code : null);
        andreiIn.Value.MarkDelta.Should().Be(0m);

        // Subscribe in the morning, snapshot the exchange in the evening — the
        // same ordinary sequence the reconciliation tripwire had to stop calling
        // a drift. No unit event claims this mark, because the subscription was
        // priced at 1,092 before the 108 existed.
        Result<AdjustBalanceResult> snapshot = await SnapshotAsync(harness, 2_200m);
        snapshot.IsSuccess.Should().BeTrue(snapshot.IsFailure ? snapshot.Error.Code : null);
        snapshot.Value.Delta.Should().Be(108m);

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        detail.AllTime.AdjustmentCount.Should().Be(1);

        // Claimed by nobody means written after everything that day, so the
        // day's CLOSING fraction is the state it was struck at: 108 x 1092/2092.
        detail.AllTime.NetPnLMdl.Should().BeApproximately(56.3748m, 0.0001m);

        // The old rule read it at the pre-subscription 1.0 and handed the owner
        // all 108.00 of a gain the friend's money was already sharing.
        detail.AllTime.NetPnLMdl.Should().NotBeApproximately(108m, 1m);
    }

    [Fact]
    public async Task Two_capital_events_on_one_day_each_price_their_own_mark()
    {
        // The case that separates "the fraction before THIS event" from every
        // whole-day approximation: one calendar day, two capital events, a mark
        // in front of each, and a different correct answer for each mark.
        var harness = PoolHarness.Create(now: Inception, openingBalance: 1_000m);

        // Seeded at the value the app already derives: no mark, and the owner
        // holds 1,000 units at a NAV of exactly 1.
        await harness.SeedPoolAsync(poolValueAtInception: 1_000m);

        harness.Advance(2);

        // MORNING. The exchange holds 1,100 against a derived 1,000, so mark
        // M1 = +100 is written first and Andrei's 500 is priced at NAV 1.10 —
        // 454.545454545455 units.
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andreiIn = await harness.SubscribeAsync(andrei, 1_100m, 500m);
        andreiIn.IsSuccess.Should().BeTrue(andreiIn.IsFailure ? andreiIn.Error.Code : null);
        andreiIn.Value.MarkDelta.Should().Be(100m);
        andreiIn.Value.NavPerUnit.Should().Be(1.1m);

        await SeparateWritesAsync();

        // AFTERNOON, same day. Derived is now 1,600 and the exchange holds
        // 1,760, so mark M2 = +160 is written first and Bogdan's 500 is priced
        // at NAV 1.21.
        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Result<RecordSubscriptionResponse> bogdanIn = await harness.SubscribeAsync(bogdan, 1_760m, 500m);
        bogdanIn.IsSuccess.Should().BeTrue(bogdanIn.IsFailure ? bogdanIn.Error.Code : null);
        bogdanIn.Value.MarkDelta.Should().Be(160m);
        bogdanIn.Value.NavPerUnit.Should().BeApproximately(1.21m, 0.0000001m);

        AccountDetailDto detail = await DetailAsync(harness, [new PoolAccountOwnershipSource(harness.Db)]);

        detail.AllTime.AdjustmentCount.Should().Be(2);

        //   M1  100.00 x 1.0                             = 100.00
        //   M2  160.00 x 1000/1454.545454545455 (0.6875)  = 110.00
        detail.AllTime.NetPnLMdl.Should().BeApproximately(210m, 0.0001m);

        // Every whole-day rule lands somewhere else, so this cannot pass by
        // accident. Pre-money-by-date (the old AddDays(-1)) reads both marks at
        // 1.0 and gives 260.00; the end-of-day fraction reads both at
        // 1000/1867.768595041323 and gives 139.20.
        detail.AllTime.NetPnLMdl.Should().NotBeApproximately(260m, 1m);
        detail.AllTime.NetPnLMdl.Should().NotBeApproximately(139.20m, 1m);

        // Both subscription legs are the friends' money arriving, so RULE 1
        // still drops them whole rather than scaling them.
        detail.AllTime.ContributionsMdl.Should().Be(0m);
        detail.AllTime.ContributionCount.Should().Be(0);
    }

    /// <summary>An ordinary MDL account with one row in every bucket.</summary>
    private static (Account Account, Transaction[] Rows) OrdinaryAccount()
    {
        Result<Account> account = Account.Create(
            "Brokerage",
            AccountType.Brokerage,
            new Money(500m, ReportingCurrencies.Mdl),
            new DateOnly(2026, 1, 1),
            notes: null);

        account.IsSuccess.Should().BeTrue();

        var date = new DateOnly(2026, 3, 10);
        Guid id = account.Value.Id;

        Transaction[] rows =
        [
            Row(id, date, TransactionDirection.Income, 1_000m, isTransfer: true, currency: ReportingCurrencies.Mdl),
            Row(id, date, TransactionDirection.Expense, 400m, isTransfer: true, currency: ReportingCurrencies.Mdl),
            Row(id, date, TransactionDirection.Income, 200m, isAdjustment: true, currency: ReportingCurrencies.Mdl),
            Row(id, date, TransactionDirection.Expense, 120m, isAdjustment: true, currency: ReportingCurrencies.Mdl),
            Row(id, date, TransactionDirection.Expense, 40m, currency: ReportingCurrencies.Mdl),
        ];

        return (account.Value, rows);
    }

    [Fact]
    public async Task Unpooled_account_is_identical_with_and_without_the_ownership_seam()
    {
        (Account account, Transaction[] rows) = OrdinaryAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], transactions: rows);

        IDateTimeProvider clock = FixedClock(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc));

        // No seam at all: literally what the handler did before this change.
        var before = new GetAccountDetailQueryHandler(db, FakeFxConverter.Identity(), [], clock);

        // The real producer against a database with no pools in it, plus a
        // registered-but-silent source. Both must be indistinguishable from the
        // line above, or every account in the app moved the day pools shipped.
        var after = new GetAccountDetailQueryHandler(
            db,
            FakeFxConverter.Identity(),
            [new PoolAccountOwnershipSource(db), FakeAccountOwnershipSource.Empty()],
            clock);

        Result<AccountDetailDto> withoutSeam =
            await before.Handle(new GetAccountDetailQuery(account.Id), CancellationToken.None);
        Result<AccountDetailDto> withSeam =
            await after.Handle(new GetAccountDetailQuery(account.Id), CancellationToken.None);

        withoutSeam.IsSuccess.Should().BeTrue();
        withSeam.IsSuccess.Should().BeTrue();

        // Whole-record equality, not a spot check: nothing anywhere in the
        // projection is allowed to move.
        withSeam.Value.Should().Be(withoutSeam.Value);

        // And the figures are the account's own, unscaled and unfiltered.
        withSeam.Value.IsPooled.Should().BeFalse();
        withSeam.Value.AllTime.ContributionsMdl.Should().Be(1_000m);
        withSeam.Value.AllTime.WithdrawalsMdl.Should().Be(400m);
        withSeam.Value.AllTime.NetPnLMdl.Should().Be(80m);
        withSeam.Value.AllTime.ContributionCount.Should().Be(1);
        withSeam.Value.AllTime.WithdrawalCount.Should().Be(1);
        withSeam.Value.AllTime.AdjustmentCount.Should().Be(2);
        withSeam.Value.RealActivityCount.Should().Be(1);
        withSeam.Value.Balance.Should().Be(1_140m);
    }

    [Fact]
    public async Task Ownership_fraction_scales_marks_only_never_transfer_legs()
    {
        // The asymmetry, stated as plainly as it can be: same account, same
        // rows, an owner fraction of 0.25 that has applied since the beginning
        // of time. A "just multiply everything" implementation reports 250 and
        // 100 here instead of 1,000 and 400.
        (Account account, Transaction[] rows) = OrdinaryAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], transactions: rows);

        var handler = new GetAccountDetailQueryHandler(
            db,
            FakeFxConverter.Identity(),
            [FakeAccountOwnershipSource.Flat(account.Id, 0.25m)],
            FixedClock(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));

        Result<AccountDetailDto> result =
            await handler.Handle(new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Transfer legs: whole or nothing. No unit event claims either of these,
        // so both stay at full size.
        result.Value.AllTime.ContributionsMdl.Should().Be(1_000m);
        result.Value.AllTime.WithdrawalsMdl.Should().Be(400m);

        // Marks: scaled. (200 - 120) x 0.25.
        result.Value.AllTime.NetPnLMdl.Should().Be(20m);

        // Counts and the balance are the ACCOUNT's, and describe it as it is.
        result.Value.AllTime.AdjustmentCount.Should().Be(2);
        result.Value.Balance.Should().Be(1_140m);

        // No pool row exists — the fraction came from a bare source. The badge
        // asks a different question and correctly answers no.
        result.Value.IsPooled.Should().BeFalse();
    }
}
