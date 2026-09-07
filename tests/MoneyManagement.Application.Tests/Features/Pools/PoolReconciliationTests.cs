using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The reconciliation tripwire: replay a pool's ledger against the account it
/// sits in and report — never repair — the five ways the two can disagree.
/// <para>
/// <see cref="PoolReconciliation.Build"/> takes plain lists plus a
/// <c>Func&lt;DateOnly, decimal&gt;</c> and so needs no <c>DbContext</c> at all.
/// The CLEAN baseline is nevertheless produced by running the real write
/// handlers through <see cref="PoolHarness"/>: a hand-built fixture that computed
/// its own units would only prove the fixture agrees with itself, and "clean"
/// is precisely the claim under test.
/// </para>
/// </summary>
public sealed class PoolReconciliationTests
{
    private static readonly DateTime Start = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Exactly what <c>GetPoolDetailQueryHandler</c> assembles before calling
    /// <see cref="PoolReconciliation.Build"/> — the same rows, the same ledger,
    /// the same as-of. Diverging here would make every assertion below a test of
    /// this method rather than of production.
    /// </summary>
    private static async Task<PoolReconciliationDto> ReconcileAsync(PoolHarness harness)
    {
        DateOnly today = harness.Today;

        Pool pool = await harness.Db.Pools
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == harness.PoolId);

        List<PoolParticipant> participants = await harness.Db.PoolParticipants
            .Where(p => p.PoolId == harness.PoolId)
            .ToListAsync();

        List<PoolUnitEvent> events = await harness.Db.PoolUnitEvents
            .Where(e => e.PoolId == harness.PoolId)
            .ToListAsync();

        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(harness.Db, CancellationToken.None);

        PoolSnapshot snapshot = PoolUnitRegister
            .Create(participants, events)
            .SnapshotAsOf(today, ledger.NativeBalanceAsOf(harness.Account, today));

        List<Transaction> rows = await harness.Db.Transactions
            .Where(t => !t.IsDeleted && t.AccountId == harness.Account.Id)
            .ToListAsync();

        PoolAccountRow[] accountRows =
        [
            .. rows.Select(t => new PoolAccountRow(
                t.Id,
                t.TransactionDate,
                t.Description,
                t.Direction,
                t.Amount.Amount,
                t.Amount.Currency,
                t.IsTransfer,
                t.IsAdjustment)),
        ];

        return PoolReconciliation.Build(
            pool,
            snapshot,
            events,
            accountRows,
            asOf => ledger.NativeBalanceAsOf(harness.Account, asOf),
            today);
    }

    /// <summary>
    /// Seed 1,092 → Andrei subscribes 1,000 → close at 2,400 → settle two days
    /// later. Every row on the account was written by a handler.
    /// </summary>
    private static async Task<PoolHarness> HandlerBuiltPoolAsync(bool settle = true)
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        Succeeded(subscription);

        harness.Advance(2);
        Result<CloseDistributionResponse> close = await harness.CloseAsync(2_400m);
        Succeeded(close);
        close.Value.MarkDelta.Should().Be(308m);

        if (settle)
        {
            harness.Advance(2);
            Result<SettleDistributionResponse> settled =
                await harness.SettleAsync(close.Value.Lines.Single().EventId);
            Succeeded(settled);
        }

        return harness;
    }

    private static Transaction ManualRow(Guid accountId, DateOnly on, decimal amount) =>
        Transaction.Create(
            accountId,
            on,
            TransactionDirection.Income,
            new Money(amount, PoolHarness.Currency),
            "Typed straight into the account",
            TransactionSource.Manual).Value;

    private static Transaction ExpenseRow(Guid accountId, DateOnly on, decimal amount) =>
        Transaction.Create(
            accountId,
            on,
            TransactionDirection.Expense,
            new Money(amount, PoolHarness.Currency),
            "Typed straight into the account",
            TransactionSource.Manual).Value;

    private static void Succeeded<T>(Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

    // ---- the clean bill --------------------------------------------------

    [Fact]
    public async Task Build_HandlerBuiltPool_IsClean()
    {
        PoolHarness harness = await HandlerBuiltPoolAsync();

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.IsClean.Should().BeTrue();
        reconciliation.UnmatchedTransactions.Should().BeEmpty();
        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.UnbackedCashClaims.Should().BeEmpty();
        reconciliation.UnitsBalance.Should().BeTrue();
        reconciliation.UnitsDrift.Should().Be(0m);
        reconciliation.ParticipantUnits.Should().Be(reconciliation.LedgerUnits);
    }

    // ---- 1. unaccounted money -------------------------------------------

    [Fact]
    public async Task Build_ManualRowOnThePoolAccount_IsUnaccounted()
    {
        PoolHarness harness = await HandlerBuiltPoolAsync();

        // Net worth reads a pooled account as `value × ownerFraction`, so 50.00
        // that mints no units is silently shared pro-rata with the friends. Every
        // path that could write this row is guarded; the row here stands for a
        // guard that was bypassed, or history that predates the guards.
        Transaction bypassed = ManualRow(harness.Account.Id, harness.Today, 50m);
        harness.Db.Transactions.Add(bypassed);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        UnmatchedPoolTransactionDto unmatched =
            reconciliation.UnmatchedTransactions.Should().ContainSingle().Which;

        unmatched.TransactionId.Should().Be(bypassed.Id);
        unmatched.Amount.Should().Be(50m);
        unmatched.Direction.Should().Be(TransactionDirection.Income);
        unmatched.IsTransfer.Should().BeFalse();

        reconciliation.IsClean.Should().BeFalse();

        // Nothing else fired: the row is dated after every priced event, so no
        // pre-money is re-derived against it.
        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.UnbackedCashClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task Build_MarkAdjustments_AreNeverUnaccounted()
    {
        PoolHarness harness = await HandlerBuiltPoolAsync();

        List<Transaction> adjustments = await harness.Db.Transactions
            .Where(t => t.AccountId == harness.Account.Id && t.IsAdjustment)
            .ToListAsync();

        // +42.00 at creation and +308.00 at the close. Both are shaped exactly
        // like a hand-typed snapshot, and neither carries a unit event: marking
        // the account to what the exchange holds moves everybody's stake
        // pro-rata, which is correct and needs no ledger entry.
        adjustments.Should().HaveCount(2);
        adjustments.Select(t => t.Amount.Amount).Should().BeEquivalentTo(new[] { 42m, 308m });
        adjustments.Should().OnlyContain(t => t.Description == "Balance adjustment");

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.UnmatchedTransactions.Should().BeEmpty();
        reconciliation.UnmatchedTransactions.Select(u => u.TransactionId)
            .Should().NotIntersectWith(adjustments.Select(t => t.Id));
    }

    [Fact]
    public async Task Build_UnpaidDistribution_IsNotUnaccounted()
    {
        // Closed, not settled. The cash has not left the account, so there is no
        // claim to match against anything — and nothing to report.
        PoolHarness harness = await HandlerBuiltPoolAsync(settle: false);

        List<PoolUnitEvent> events = await harness.Db.PoolUnitEvents.ToListAsync();
        PoolUnitEvent distribution = events.Single(e => e.Kind == PoolUnitEventKind.Distribution);
        distribution.SettledOn.Should().BeNull();
        distribution.MovementTransactionId.Should().BeNull();
        distribution.Cash!.Value.Amount.Should().BeGreaterThan(0m);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.IsClean.Should().BeTrue();
        reconciliation.UnmatchedTransactions.Should().BeEmpty();
        reconciliation.UnbackedCashClaims.Should().BeEmpty();
        reconciliation.ValueDrifts.Should().BeEmpty();
    }

    [Fact]
    public async Task Build_TransactionDatedExactlyOnInception_IsNotUnaccounted()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        harness.Advance(2);

        // Inception day and earlier is the seed's territory: the balance standing
        // there BECAME the owner's units, so no row up to and including that date
        // needs an event of its own. One day later it does.
        Transaction onInception = ManualRow(harness.Account.Id, PoolWorkedExample.Inception, 25m);
        Transaction dayAfter = ManualRow(harness.Account.Id, PoolWorkedExample.Inception.AddDays(1), 30m);

        harness.Db.Transactions.Add(onInception);
        harness.Db.Transactions.Add(dayAfter);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        UnmatchedPoolTransactionDto unmatched =
            reconciliation.UnmatchedTransactions.Should().ContainSingle().Which;

        unmatched.TransactionId.Should().Be(dayAfter.Id);
        unmatched.Amount.Should().Be(30m);
    }

    // ---- 2. Σ participantUnits == totalUnits -----------------------------

    [Fact]
    public async Task Build_ParticipantMissingFromTheRoster_BreaksTheUnitsIdentity()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        Succeeded(subscription);
        subscription.Value.Units.Should().Be(1_000m);

        // The roster row disappears; his events do not. Only assertable because
        // the OWNER holds real units instead of being modelled as a residual —
        // as a residual, Σ participantUnits == totalUnits would be true by
        // construction and would prove nothing.
        PoolParticipant row = await harness.Db.PoolParticipants.SingleAsync(p => p.Id == andrei);
        harness.Db.PoolParticipants.Remove(row);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.UnitsBalance.Should().BeFalse();
        reconciliation.UnitsDrift.Should().Be(-1_000m);
        reconciliation.ParticipantUnits.Should().Be(1_092m);
        reconciliation.LedgerUnits.Should().Be(2_092m, "the total is summed from the EVENTS, not the positions");
        reconciliation.IsClean.Should().BeFalse();
    }

    // ---- 3. recorded pre-money vs. re-derived pre-money ------------------

    [Fact]
    public async Task Build_DeletedMarkBeforeAPricedEvent_ReportsAValueDrift()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughBogdanAsync();

        // §6's Sep-12 step: Binance read 2,175.68 against a derived 2,092.00, so
        // the mark went first and THEN Bogdan's units were priced at 1.04. Remove
        // the mark and the 83.68 that justified that price is gone — while 961.53
        // units minted against it are not.
        Transaction mark = await example.Db.Transactions
            .SingleAsync(t => t.Id == example.BogdanMarkTransactionId!.Value);

        mark.Amount.Amount.Should().Be(PoolWorkedExample.BogdanMarkDelta);
        example.Db.Transactions.Remove(mark);

        PoolReconciliationDto reconciliation = await ReconcileAsync(example.Harness);

        PoolValueDriftDto drift = reconciliation.ValueDrifts.Should().ContainSingle().Which;

        drift.OccurredOn.Should().Be(PoolWorkedExample.BogdanSubscribes);
        drift.Kind.Should().Be(PoolUnitEventKind.Subscription);
        drift.RecordedPreMoney.Should().Be(2_175.68m);
        drift.DerivedPreMoney.Should().Be(2_092.00m);
        drift.Drift.Should().Be(83.68m);

        reconciliation.IsClean.Should().BeFalse();

        // Andrei's own event still reproduces: the deleted row is dated after it.
        reconciliation.ValueDrifts.Should().NotContain(d => d.OccurredOn == PoolWorkedExample.AndreiSubscribes);
    }

    /// <summary>
    /// The false positive the replay used to raise, pinned closed.
    /// <para>
    /// <c>balanceAsOf</c> answers END-OF-DAY, so a re-pricing MARK written later
    /// on a priced event's own day sits inside the balance the replay starts
    /// from. Undoing only the CASH of unit events left it there, and every event
    /// already recorded that day reported a drift exactly equal to the mark.
    /// </para>
    /// <para>
    /// Reachable through the shipped write slice with no guard bypassed at all:
    /// <c>AdjustBalance</c> explicitly ALLOWS an Adjustment dated today on a
    /// pooled account (that is the one thing a pooled account must still accept).
    /// So "subscribe in the morning, snapshot the exchange in the evening" left
    /// the pool detail page reporting a value drift on a pool where nothing
    /// whatsoever was wrong — a false alarm on the one tripwire the design tells
    /// the user to trust, on a completely ordinary sequence.
    /// </para>
    /// <para>
    /// Previously observed: one drift on the subscription,
    /// <c>RecordedPreMoney = 1092</c>, <c>DerivedPreMoney = 1200</c>,
    /// <c>Drift = -108</c> — exactly the mark. The fix strips the day's unclaimed
    /// marks and lets each event claim only the ones its own pre-money implies;
    /// this one implies none, so the 108 is never reached.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Build_MarkWrittenAfterAPricedEventOnTheSameDay_IsNotADrift()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_092m, 1_000m));

        // A perfectly legal snapshot, same day, through the real handler.
        Result<AdjustBalanceResult> mark =
            await new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock).Handle(
                new AdjustBalanceCommand(
                    harness.Account.Id,
                    BalanceChangeKind.Adjustment,
                    2_200m,
                    harness.Today,
                    Notes: null),
                CancellationToken.None);

        Succeeded(mark);
        mark.Value.Delta.Should().Be(108m);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.ValueDrifts.Should().BeEmpty(
            "the subscription was priced at 1,092 before the 108 mark existed, and nothing about it changed");
        reconciliation.IsClean.Should().BeTrue();
    }

    /// <summary>
    /// The same defect through its other door: the mark that lands after a priced
    /// event on the same day is written by a CLOSE rather than by a hand-typed
    /// snapshot.
    /// <para>
    /// Subscribe in the morning at 1,092 pre-money, close the month in the
    /// evening against a real 2,400 — which writes a +308 mark — and pay it out
    /// the same day. The subscription must still reproduce at 1,092: the 308 did
    /// not exist when it was priced. The close's own line must reproduce at 2,400,
    /// which means the 308 DOES have to be claimed, by the event that was actually
    /// struck against it. One day, two events, one mark, and each has to see a
    /// different balance.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Build_CloseLandingAfterASubscriptionOnTheSameDay_IsNotADrift()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_092m, 1_000m));

        Result<CloseDistributionResponse> close = await harness.CloseAsync(2_400m);
        Succeeded(close);
        close.Value.MarkDelta.Should().Be(308m, "the account derived 2,092 and the exchange really held 2,400");
        close.Value.PoolValuePreMoney.Should().Be(2_400m);

        // Paid the same day too, so the payout row is in the end-of-day balance
        // as well — the harshest arrangement of the three rows.
        Succeeded(await harness.SettleAsync(close.Value.Lines.Single().EventId));

        List<PoolUnitEvent> events = await harness.Db.PoolUnitEvents.ToListAsync();
        events.Select(e => e.OccurredOn)
            .Where(d => d == harness.Today)
            .Should().HaveCount(2, "the subscription and the distribution share a day");

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.IsClean.Should().BeTrue();
    }

    /// <summary>
    /// The counterpart, and the reason the fix is not simply "ignore adjustments":
    /// a mark written BEFORE the event is part of that event's pre-money and must
    /// still be reproduced, several of them at once if that is how they landed.
    /// <para>
    /// Two snapshots (+30, then +20) and then a subscription priced against the
    /// marked 1,142. The claim takes them as a prefix summing to +50 — and a
    /// deleted mark still fails to reconcile, which is the whole point of the
    /// check.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Build_TwoMarksBeforeAPricedEventOnTheSameDay_AreBothClaimed()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);

        var adjust = new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Succeeded(await adjust.Handle(
            new AdjustBalanceCommand(harness.Account.Id, BalanceChangeKind.Adjustment, 1_122m, harness.Today, Notes: null),
            CancellationToken.None));

        Succeeded(await adjust.Handle(
            new AdjustBalanceCommand(harness.Account.Id, BalanceChangeKind.Adjustment, 1_142m, harness.Today, Notes: null),
            CancellationToken.None));

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_142m, 1_000m);
        Succeeded(subscription);
        subscription.Value.MarkDelta.Should().Be(0m, "the two snapshots already brought the account to 1,142");

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.IsClean.Should().BeTrue();

        // …and losing one of them still breaks the replay. The claim is not a
        // blanket amnesty for adjustment rows: it has to add up.
        Transaction dropped = await harness.Db.Transactions
            .Where(t => t.IsAdjustment && t.TransactionDate == harness.Today)
            .OrderBy(t => t.Id)
            .FirstAsync();

        dropped.Amount.Amount.Should().Be(30m);
        harness.Db.Transactions.Remove(dropped);

        PoolValueDriftDto drift = (await ReconcileAsync(harness)).ValueDrifts.Should().ContainSingle().Which;

        drift.Kind.Should().Be(PoolUnitEventKind.Subscription);
        drift.RecordedPreMoney.Should().Be(1_142m);
        drift.DerivedPreMoney.Should().Be(1_092m);
        drift.Drift.Should().Be(50m);
    }

    [Fact]
    public async Task Build_TwoLineClose_IsClean()
    {
        PoolHarness harness = await TwoFriendsAsync();

        // ONE close, two lines, one pre-money — and both paid out the same day,
        // which is the only arrangement in which the replay has to undo a SIBLING
        // line's cash as well as its own. Miss that and the second line reports
        // itself as drifted by the first line's payout.
        harness.Advance(2);
        Result<CloseDistributionResponse> close = await harness.CloseAsync(3_400m);
        Succeeded(close);
        close.Value.Lines.Should().HaveCount(2);

        foreach (DistributionLine line in close.Value.Lines)
        {
            Succeeded(await harness.SettleAsync(line.EventId));
        }

        List<PoolUnitEvent> distributions = await harness.Db.PoolUnitEvents
            .Where(e => e.Kind == PoolUnitEventKind.Distribution)
            .ToListAsync();

        distributions.Should().HaveCount(2);
        distributions.Select(e => e.OccurredOn).Distinct().Should().ContainSingle();
        distributions.Select(e => e.SettledOn).Should().AllBeEquivalentTo((DateOnly?)harness.Today);
        distributions.Select(e => e.PoolValuePreMoney).Distinct()
            .Should().ContainSingle("every line of one close shares a single pre-money — that IS the close's identity");

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Build_TwoSeparateClosesOnOneDay_IsClean()
    {
        PoolHarness harness = await TwoFriendsAsync();
        Guid andrei = await ParticipantIdAsync(harness, "Andrei");
        Guid bogdan = await ParticipantIdAsync(harness, "Bogdan");

        harness.Advance(2);

        // Close #1 pays Andrei only, and is settled immediately.
        Result<CloseDistributionResponse> first =
            await harness.CloseAsync(3_400m, [new DistributionPayout(andrei)]);
        Succeeded(first);
        Succeeded(await harness.SettleAsync(first.Value.Lines.Single().EventId));

        // Close #2, SAME DAY, pays Bogdan against what is left. Its pre-money is
        // necessarily lower — the first close's payout already went out — so the
        // two are different closes that happen to share a date. Exempting sibling
        // lines by CALENDAR DAY would have these two cancel each other out.
        decimal remaining = 3_400m - first.Value.TotalCash;
        Result<CloseDistributionResponse> second =
            await harness.CloseAsync(remaining, [new DistributionPayout(bogdan)]);
        Succeeded(second);
        Succeeded(await harness.SettleAsync(second.Value.Lines.Single().EventId));

        first.Value.PoolValuePreMoney.Should().NotBe(second.Value.PoolValuePreMoney);

        List<PoolUnitEvent> distributions = await harness.Db.PoolUnitEvents
            .Where(e => e.Kind == PoolUnitEventKind.Distribution)
            .ToListAsync();

        distributions.Select(e => e.OccurredOn).Distinct().Should().ContainSingle();
        distributions.Select(e => e.PoolValuePreMoney).Distinct().Should().HaveCount(2);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.IsClean.Should().BeTrue();
    }

    // ---- 4. unit events claiming cash the account never saw ---------------

    [Fact]
    public async Task Build_UnitEventClaimingCashThatNeverLanded_IsReported()
    {
        // The phantom backfill. CreatePool may replay a subscription from before
        // the pool existed with WriteMovementTransaction = false, on the promise
        // that the arrival was already on the account. Here it never was — and
        // 500 units were minted against it anyway.
        var harness = PoolHarness.Create(
            now: PoolWorkedExample.Inception.AddDays(2).ToDateTime(new TimeOnly(12, 0)),
            openingBalance: 1_050m);

        DateOnly arrival = PoolWorkedExample.Inception.AddDays(1);

        await harness.SeedPoolAsync(
            inceptionDate: PoolWorkedExample.Inception,
            poolValueAtInception: 1_092m,
            backfills: [new BackdatedSubscription("Andrei", arrival, 500m, 1_092m)]);

        PoolUnitEvent backfilled = await harness.Db.PoolUnitEvents
            .SingleAsync(e => e.Kind == PoolUnitEventKind.Subscription);

        backfilled.MovementTransactionId.Should().BeNull();
        backfilled.SettledOn.Should().Be(arrival);

        // Stamp what the audit interceptor stamps in production. It makes the
        // event BACK-DATED, which is what the pre-money replay deliberately gives
        // up on — so this check is the only one left that can see the phantom.
        backfilled.CreatedAt = harness.Clock.UtcNow;

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        UnbackedPoolCashClaimDto claim =
            reconciliation.UnbackedCashClaims.Should().ContainSingle().Which;

        claim.EventId.Should().Be(backfilled.Id);
        claim.SettledOn.Should().Be(arrival);
        claim.Direction.Should().Be(TransactionDirection.Income);
        claim.Amount.Should().Be(500m);

        reconciliation.IsClean.Should().BeFalse();

        // And it really is invisible to the other three: nothing on the account
        // is unexplained, the units add up, and the pre-money was never checked.
        reconciliation.UnmatchedTransactions.Should().BeEmpty();
        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.UnitsBalance.Should().BeTrue();
    }

    // ---- 5. the ledger's predicted balance vs. the account's real one -----

    /// <summary>"2026-09-01" — the live pool's inception, on the repo's shifted calendar.</summary>
    private static readonly DateOnly LiveInception = new(2026, 6, 1);

    /// <summary>
    /// THE LIVE BLIND SPOT, reproduced exactly, and the regression this whole
    /// change exists for.
    /// <para>
    /// A pool set up from history with two same-day backfills where only ONE of
    /// them had <c>WriteMovementTransaction</c> set: the owner's own 1,000
    /// funding transfer, the second friend's 1,000 subscription leg, and nothing
    /// at all for the first friend — whose 1,000 units were minted anyway. Three
    /// thousand units against a two-thousand balance, NAV collapsed to 0.667,
    /// and <c>reconciliation.isClean</c> came back TRUE.
    /// </para>
    /// <para>
    /// It cleared the tripwire because the phantom claim (inception day, Income,
    /// 1,000) matched the OWNER'S funding transfer — a different real row, same
    /// date, same direction, same amount — consumed it, and left the leftover
    /// list empty. Both halves of the fix are asserted below: the claim can no
    /// longer eat the seed's backing, AND the ledger's own arithmetic names the
    /// missing 1,000 without needing to tell the two rows apart at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Build_BackfillWhoseMoneyNeverLanded_IsReportedEvenThoughAnotherRowLooksIdentical()
    {
        PoolHarness harness = await LivePoolAsync(writeFelixLeg: false);

        PoolSnapshot snapshot = await harness.SnapshotAsync();
        snapshot.TotalUnits.Should().Be(3_000m);
        snapshot.AccountBalance.Should().Be(2_000m);
        snapshot.NavPerUnit.Should().Be(0.666666666667m, "1,000 of the 3,000 units is backed by nothing");

        PoolUnitEvent felix = await EventForAsync(harness, "Felix");
        felix.MovementTransactionId.Should().BeNull("this is the flag that went unset in production");
        felix.SettledOn.Should().Be(LiveInception);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.IsClean.Should().BeFalse();

        // FIX 1 — the seed's backing is reserved before any claim may touch it,
        // so the owner's funding transfer is no longer available to explain
        // Felix's money.
        UnbackedPoolCashClaimDto claim =
            reconciliation.UnbackedCashClaims.Should().ContainSingle().Which;

        claim.EventId.Should().Be(felix.Id);
        claim.SettledOn.Should().Be(LiveInception);
        claim.Direction.Should().Be(TransactionDirection.Income);
        claim.Amount.Should().Be(1_000m);

        // FIX 2 — and the identity says the same thing on its own terms, without
        // matching anything to anything.
        reconciliation.PredictedBalance.Should().Be(3_000m, "seed 1,000 + two subscriptions of 1,000");
        reconciliation.DerivedBalance.Should().Be(2_000m);
        reconciliation.BalanceDrift.Should().Be(1_000m, "exactly the leg that was never written");
        reconciliation.BalanceReconciles.Should().BeFalse();

        // The owner's transfer is still the seed's territory, not a finding of
        // its own — reserving it must not turn it into noise.
        reconciliation.UnmatchedTransactions.Should().BeEmpty();

        // And it really was invisible to the other checks: the units add up, and
        // both subscriptions are back-dated, which the pre-money replay skips.
        reconciliation.UnitsBalance.Should().BeTrue();
        reconciliation.ValueDrifts.Should().BeEmpty();
    }

    /// <summary>
    /// The same pool, built correctly. This one matters as much as the failure
    /// above: a tripwire that fires on healthy data gets ignored, and then the
    /// finding that matters goes unread.
    /// </summary>
    [Fact]
    public async Task Build_TheSamePoolWithBothLegsWritten_IsClean()
    {
        PoolHarness harness = await LivePoolAsync(writeFelixLeg: true);

        PoolSnapshot snapshot = await harness.SnapshotAsync();
        snapshot.TotalUnits.Should().Be(3_000m);
        snapshot.AccountBalance.Should().Be(3_000m);
        snapshot.NavPerUnit.Should().Be(1m);

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.IsClean.Should().BeTrue();
        reconciliation.PredictedBalance.Should().Be(3_000m);
        reconciliation.DerivedBalance.Should().Be(3_000m);
        reconciliation.BalanceDrift.Should().Be(0m);
        reconciliation.BalanceReconciles.Should().BeTrue();
        reconciliation.UnbackedCashClaims.Should().BeEmpty();
        reconciliation.UnmatchedTransactions.Should().BeEmpty();
        reconciliation.ValueDrifts.Should().BeEmpty();
    }

    /// <summary>
    /// An UNPAID payout must stay clean. It has closed — the units are gone —
    /// but the cash is still sitting in the account, so subtracting it would
    /// report a gap on every pool between month-end and payday: the loudest
    /// possible false alarm, once a month, forever.
    /// </summary>
    [Fact]
    public async Task Build_ClosedButUnsettledDistribution_StillReconciles()
    {
        PoolHarness harness = await HandlerBuiltPoolAsync(settle: false);

        PoolUnitEvent distribution = await harness.Db.PoolUnitEvents
            .SingleAsync(e => e.Kind == PoolUnitEventKind.Distribution);

        distribution.SettledOn.Should().BeNull();
        decimal owed = distribution.Cash!.Value.Amount;
        owed.Should().BeGreaterThan(0m);

        PoolReconciliationDto unpaid = await ReconcileAsync(harness);

        unpaid.IsClean.Should().BeTrue();
        unpaid.BalanceReconciles.Should().BeTrue();
        unpaid.PredictedBalance.Should().Be(2_400m, "the payout money has not left the account yet");
        unpaid.DerivedBalance.Should().Be(2_400m);

        // …and once it does leave, both sides fall by the same amount.
        harness.Advance(2);
        Succeeded(await harness.SettleAsync(distribution.Id));

        PoolReconciliationDto settled = await ReconcileAsync(harness);

        settled.IsClean.Should().BeTrue();
        settled.PredictedBalance.Should().Be(2_400m - owed);
        settled.DerivedBalance.Should().Be(2_400m - owed);
        settled.BalanceDrift.Should().Be(0m);
    }

    /// <summary>
    /// An account with a life BEFORE the pool must stay clean. That history is
    /// inside the balance the seed's units were struck from, so the prediction
    /// starts there and never enumerates it — an account carrying years of rows
    /// reconciles exactly like an empty one.
    /// </summary>
    [Fact]
    public async Task Build_AccountWithRowsPredatingInception_IsClean()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);

        // Ordinary movements from before anybody pooled this account.
        harness.Db.Transactions.Add(ManualRow(harness.Account.Id, DateOnly.FromDateTime(Start.AddDays(-30)), 300m));
        harness.Db.Transactions.Add(ExpenseRow(harness.Account.Id, DateOnly.FromDateTime(Start.AddDays(-10)), 120m));

        (await harness.DerivedBalanceAsync()).Should().Be(1_230m);

        // The exchange really held 1,300 on inception day: a +70 catch-up mark,
        // then 1,300 units to the owner.
        CreatePoolResponse created = await harness.SeedPoolAsync(poolValueAtInception: 1_300m);
        created.MarkDelta.Should().Be(70m);
        created.SeedUnits.Should().Be(1_300m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_300m, 1_000m));

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.IsClean.Should().BeTrue();
        reconciliation.PredictedBalance.Should().Be(2_300m);
        reconciliation.DerivedBalance.Should().Be(2_300m);
        reconciliation.UnmatchedTransactions.Should().BeEmpty(
            "rows before inception are the seed's territory, not findings");
    }

    /// <summary>
    /// The false positive the balance check would raise if it were anchored on
    /// the SEED'S STORED VALUE instead of on the account, pinned closed.
    /// <para>
    /// Create a pool dated today and record a subscription the same day, and
    /// <c>PoolMark</c> writes a re-pricing mark dated ON the inception date,
    /// AFTER the seed was struck — through the shipped write slice, with no
    /// guard bypassed. Read off the seed, every such pool would report a gap
    /// equal to that mark from then on. Read off <c>balanceAsOf(inception)</c>
    /// the mark is simply inside the anchor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Build_MarkDatedOnInceptionDayAfterTheSeed_IsClean()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        CreatePoolResponse created = await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        created.SeedUnits.Should().Be(1_092m);

        // Same day as inception, no clock advance.
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_200m, 1_000m);
        Succeeded(subscription);
        subscription.Value.MarkDelta.Should().Be(108m, "the exchange had moved since the pool was created");

        List<Transaction> marksOnInception = await harness.Db.Transactions
            .Where(t => t.IsAdjustment && t.TransactionDate == harness.Today)
            .ToListAsync();

        marksOnInception.Should().HaveCount(2, "the +42 catch-up AND the +108 written after the seed");

        PoolReconciliationDto reconciliation = await ReconcileAsync(harness);

        reconciliation.IsClean.Should().BeTrue();
        reconciliation.PredictedBalance.Should().Be(2_200m);
        reconciliation.DerivedBalance.Should().Be(2_200m);
        reconciliation.BalanceDrift.Should().Be(0m);
    }

    /// <summary>
    /// The reservation is a BUDGET, not a ban — and the two checks agree about
    /// which inception-day rows the ledger owns.
    /// <para>
    /// A backfilled subscription dated on the inception date itself, with no
    /// movement row of its own, against an account that really did receive that
    /// money on that day. There is 500 at inception over and above the seed's
    /// 1,092, so the claim may consume it: no unbacked claim, and the balance
    /// identity takes the same row off the seed rather than counting it twice.
    /// Delete the row and both checks flip together.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Build_InceptionDayClaimBackedByRealSurplus_IsClean_AndBreaksWhenTheRowGoes()
    {
        var harness = PoolHarness.Create(
            now: PoolWorkedExample.Inception.AddDays(2).ToDateTime(new TimeOnly(12, 0)),
            openingBalance: 1_050m);

        await harness.SeedPoolAsync(
            inceptionDate: PoolWorkedExample.Inception,
            poolValueAtInception: 1_092m,
            backfills:
            [
                new BackdatedSubscription("Andrei", PoolWorkedExample.Inception, 500m, 1_092m),
            ]);

        PoolUnitEvent backfilled = await EventForAsync(harness, "Andrei");
        backfilled.MovementTransactionId.Should().BeNull();
        backfilled.CreatedAt = harness.Clock.UtcNow;

        // Andrei's 500 really did land on inception day — it was just entered by
        // hand before the pool existed, which is exactly why the backfill was
        // recorded with WriteMovementTransaction = false.
        Transaction arrival = ManualRow(harness.Account.Id, PoolWorkedExample.Inception, 500m);
        harness.Db.Transactions.Add(arrival);

        PoolReconciliationDto backed = await ReconcileAsync(harness);

        backed.IsClean.Should().BeTrue();
        backed.UnbackedCashClaims.Should().BeEmpty("there is 500 at inception the seed does not claim");
        backed.PredictedBalance.Should().Be(1_592m);
        backed.DerivedBalance.Should().Be(1_592m);

        // Take the money away and there is no surplus left to consume: the claim
        // is reported, and the identity reports the same 500 independently.
        harness.Db.Transactions.Remove(arrival);

        PoolReconciliationDto phantom = await ReconcileAsync(harness);

        phantom.IsClean.Should().BeFalse();
        phantom.UnbackedCashClaims.Should().ContainSingle().Which.Amount.Should().Be(500m);
        phantom.PredictedBalance.Should().Be(1_592m);
        phantom.DerivedBalance.Should().Be(1_092m);
        phantom.BalanceDrift.Should().Be(500m);
    }

    /// <summary>
    /// Owner funds the account on inception day, then TWO friends are backfilled
    /// on that same day at 1,000 each. <paramref name="writeFelixLeg"/> is the
    /// single flag that went unset in production.
    /// </summary>
    private static async Task<PoolHarness> LivePoolAsync(bool writeFelixLeg)
    {
        // Created six days AFTER inception, exactly like the real one — which is
        // what makes both subscriptions back-dated and therefore invisible to the
        // pre-money replay.
        var harness = PoolHarness.Create(
            now: LiveInception.AddDays(6).ToDateTime(new TimeOnly(12, 0)),
            openingBalance: 0m);

        // The owner's OWN money arriving, before the pool existed. This is the
        // row the phantom claim matched on and consumed.
        harness.Db.Transactions.Add(ManualRow(harness.Account.Id, LiveInception, 1_000m));

        await harness.SeedPoolAsync(
            inceptionDate: LiveInception,
            poolValueAtInception: 1_000m,
            backfills:
            [
                new BackdatedSubscription("Felix", LiveInception, 1_000m, 1_000m, writeFelixLeg),
                new BackdatedSubscription("Student B", LiveInception, 1_000m, 2_000m, WriteMovementTransaction: true),
            ]);

        // What the audit interceptor stamps in production: recorded today,
        // occurred six days ago.
        foreach (PoolUnitEvent unitEvent in await harness.Db.PoolUnitEvents.ToListAsync())
        {
            unitEvent.CreatedAt = harness.Clock.UtcNow;
        }

        return harness;
    }

    private static async Task<PoolUnitEvent> EventForAsync(PoolHarness harness, string participantName)
    {
        PoolParticipant participant = await harness.Db.PoolParticipants
            .SingleAsync(p => p.PoolId == harness.PoolId && p.Name == participantName);

        return await harness.Db.PoolUnitEvents.SingleAsync(e => e.ParticipantId == participant.Id);
    }

    // ---- shared fixtures --------------------------------------------------

    /// <summary>Seed 1,092 → Andrei 1,000 → Bogdan 1,000, all at a NAV of exactly 1.</summary>
    private static async Task<PoolHarness> TwoFriendsAsync()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_092m, 1_000m));

        harness.Advance(2);
        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Succeeded(await harness.SubscribeAsync(bogdan, 2_092m, 1_000m));

        (await harness.DerivedBalanceAsync()).Should().Be(3_092m);
        return harness;
    }

    private static async Task<Guid> ParticipantIdAsync(PoolHarness harness, string name)
    {
        PoolParticipant participant = await harness.Db.PoolParticipants
            .SingleAsync(p => p.PoolId == harness.PoolId && p.Name == name);

        return participant.Id;
    }
}
