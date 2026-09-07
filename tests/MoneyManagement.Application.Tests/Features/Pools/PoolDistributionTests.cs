using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The two-phase payout, and the stored-field-less high-water mark that decides
/// how much of it there is.
/// </summary>
public class PoolDistributionTests
{
    /// <summary>
    /// Owner seeded at 1,200; Andrei and Bogdan each in for 1,000, both struck
    /// at NAV 1.0. Pool value 3,200, total units 3,200.
    /// <para>
    /// The owner's 1,200 is deliberately NOT 1,000: a subscription whose cash
    /// equals the account's balance to the cent is refused outright
    /// (<c>pools.cash_looks_like_a_balance</c>), because that is the shape of the
    /// demonstrated data-entry slip.
    /// </para>
    /// </summary>
    private static async Task<(PoolHarness Harness, Guid Andrei, Guid Bogdan)> TwoFriendsAtParAsync()
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> first = await harness.SubscribeAsync(andrei, 1_200m, 1_000m);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Code : null);

        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Result<RecordSubscriptionResponse> second = await harness.SubscribeAsync(bogdan, 2_200m, 1_000m);
        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Code : null);

        return (harness, andrei, bogdan);
    }

    [Fact]
    public async Task Close_BurnsUnitsAtNav_RecordsCashOwed_AndWritesNoTransaction()
    {
        (PoolHarness harness, Guid andrei, Guid bogdan) = await TwoFriendsAtParAsync();

        int transactionsBefore = (await harness.Db.Transactions.ToListAsync()).Count;

        // +10%: 3,200 becomes 3,520, so each friend is 100 above basis.
        Result<CloseDistributionResponse> close = await harness.CloseAsync(3_520m);
        close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.Code : null);

        close.Value.NavPerUnit.Should().Be(1.1m);
        close.Value.TotalCash.Should().Be(200m);
        close.Value.Lines.Should().HaveCount(2);
        close.Value.Lines.Select(l => l.ParticipantId).Should().BeEquivalentTo([andrei, bogdan]);
        close.Value.Lines.Should().AllSatisfy(l =>
        {
            l.Cash.Should().Be(100m);
            l.Units.Should().Be(decimal.Round(100m / 1.1m, 12));
        });

        List<Transaction> after = await harness.Db.Transactions.ToListAsync();

        // The mark is the ONLY row a close writes. No payout transaction exists
        // yet — that is phase two.
        after.Should().HaveCount(transactionsBefore + 1);
        after.Should().NotContain(t => t.CategoryId == SeededCategories.PoolId
            && t.Direction == TransactionDirection.Expense);

        List<PoolUnitEvent> distributions = await harness.Db.PoolUnitEvents
            .Where(e => e.Kind == PoolUnitEventKind.Distribution)
            .ToListAsync();

        distributions.Should().HaveCount(2);
        distributions.Should().AllSatisfy(e =>
        {
            e.SettledOn.Should().BeNull();
            e.MovementTransactionId.Should().BeNull();
        });
    }

    [Fact]
    public async Task Close_PoolValueExcludesTheUnpaidCash_SoNavIsUnchanged()
    {
        (PoolHarness harness, _, _) = await TwoFriendsAtParAsync();

        await harness.CloseAsync(3_520m);

        PoolSnapshot after = await harness.SnapshotAsync();

        // The USDT is still physically in the account (balance 3,300), but 200
        // of it is owed. Without the subtraction the friends would be paid on
        // that same 200 again next month.
        (await harness.DerivedBalanceAsync()).Should().Be(3_520m);
        after.UnpaidDistributionCash.Should().Be(200m);
        after.PoolValue.Should().Be(3_320m);
        after.NavPerUnit.Should().Be(1.1m);
    }

    [Fact]
    public async Task Close_SweepsBothFriendsBackToExactlyTheirCapitalBase()
    {
        (PoolHarness harness, Guid andrei, Guid bogdan) = await TwoFriendsAtParAsync();

        await harness.CloseAsync(3_520m);

        PoolSnapshot after = await harness.SnapshotAsync();

        // No line of code says "reset their base" — it falls out of redeeming at
        // NAV, which is the whole reason the high-water mark needs no field.
        after.Find(andrei)!.Stake.Should().Be(1_000m);
        after.Find(bogdan)!.Stake.Should().Be(1_000m);
        after.Find(andrei)!.CapitalBase.Should().Be(1_000m);
        after.Find(andrei)!.Distributable.Should().Be(0m);
        after.Find(bogdan)!.Distributable.Should().Be(0m);
    }

    [Fact]
    public async Task Settle_WritesThePaymentOnTheDayTheMoneyActuallyLeft()
    {
        (PoolHarness harness, Guid andrei, _) = await TwoFriendsAtParAsync();

        Result<CloseDistributionResponse> close = await harness.CloseAsync(
            3_520m,
            [new DistributionPayout(andrei)]);

        close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.Code : null);
        Guid eventId = close.Value.Lines.Single().EventId;

        // The month closed on the 30th; the USDT leaves on the 2nd.
        DateOnly closeDate = harness.Today;
        harness.Advance(3);
        DateOnly payDate = harness.Today;

        Result<SettleDistributionResponse> settle = await harness.SettleAsync(eventId);
        settle.IsSuccess.Should().BeTrue(settle.IsFailure ? settle.Error.Code : null);
        settle.Value.SettledOn.Should().Be(payDate);
        settle.Value.Cash.Should().Be(100m);

        Transaction payment = (await harness.Db.Transactions.ToListAsync())
            .Single(t => t.Id == settle.Value.TransactionId);

        payment.TransactionDate.Should().Be(payDate);
        payment.TransactionDate.Should().NotBe(closeDate);
        payment.Direction.Should().Be(TransactionDirection.Expense);
        payment.IsTransfer.Should().BeTrue();
        payment.IsAdjustment.Should().BeFalse();
        payment.CategoryId.Should().Be(SeededCategories.PoolId);
        payment.CounterAccountId.Should().BeNull();
        payment.Description.Should().Be("Pool payout to Andrei");

        PoolUnitEvent unitEvent = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Id == eventId);

        unitEvent.SettledOn.Should().Be(payDate);
        unitEvent.MovementTransactionId.Should().Be(payment.Id);

        // Once paid, the cash has physically gone: the balance falls and there
        // is nothing left to subtract.
        PoolSnapshot after = await harness.SnapshotAsync();
        after.UnpaidDistributionCash.Should().Be(0m);
        (await harness.DerivedBalanceAsync()).Should().Be(3_420m);
        after.PoolValue.Should().Be(3_420m);
    }

    [Fact]
    public async Task Settle_Twice_IsRejected()
    {
        (PoolHarness harness, Guid andrei, _) = await TwoFriendsAtParAsync();

        Result<CloseDistributionResponse> close =
            await harness.CloseAsync(3_520m, [new DistributionPayout(andrei)]);

        Guid eventId = close.Value.Lines.Single().EventId;

        (await harness.SettleAsync(eventId)).IsSuccess.Should().BeTrue();

        Result<SettleDistributionResponse> again = await harness.SettleAsync(eventId);
        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("pools.distribution_already_settled");
    }

    [Fact]
    public async Task Distributable_IsZeroBelowBasis_AndCorrectAfterALossThenRecovery()
    {
        (PoolHarness harness, Guid andrei, _) = await TwoFriendsAtParAsync();

        // October: −15%. Every stake is under water.
        PoolSnapshot down = await SnapshotAtAsync(harness, 2_720m);
        down.Find(andrei)!.Stake.Should().Be(850m);
        down.Find(andrei)!.Distributable.Should().Be(0m);

        // November: +10% off the bottom. GREEN MONTH, still below the 1,000
        // base, so nothing is payable — the cost of the high-water mark, stated
        // in the message to the friends.
        PoolSnapshot recovering = await SnapshotAtAsync(harness, 2_992m);
        recovering.Find(andrei)!.Stake.Should().Be(935m);
        recovering.Find(andrei)!.Distributable.Should().Be(0m);

        Result<CloseDistributionResponse> nothingToPay = await harness.CloseAsync(2_992m);
        nothingToPay.IsFailure.Should().BeTrue();
        nothingToPay.Error.Code.Should().Be("pools.distribution_nothing_to_pay");

        // December: back above basis. Only the excess is payable, not the whole
        // month's gain.
        PoolSnapshot above = await SnapshotAtAsync(harness, 3_360m);
        above.Find(andrei)!.Stake.Should().Be(1_050m);
        above.Find(andrei)!.Distributable.Should().Be(50m);
    }

    [Fact]
    public async Task SkippedPayout_Accumulates_RatherThanResetting()
    {
        (PoolHarness harness, Guid andrei, Guid bogdan) = await TwoFriendsAtParAsync();

        // Month one: Andrei takes his 100; Bogdan says "leave it in".
        Result<CloseDistributionResponse> first =
            await harness.CloseAsync(3_520m, [new DistributionPayout(andrei)]);

        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Code : null);

        PoolSnapshot afterFirst = await harness.SnapshotAsync();
        afterFirst.Find(andrei)!.Distributable.Should().Be(0m);
        afterFirst.Find(bogdan)!.Distributable.Should().Be(100m, "reinvestment needs no code — he simply kept his units");

        // Pay Andrei so the owed cash leaves the account.
        harness.Advance(2);
        await harness.SettleAsync(first.Value.Lines.Single().EventId);

        // Month two: the pool runs up again.
        harness.Advance(28);
        PoolSnapshot afterSecond = await SnapshotAtAsync(harness, 3_700m);

        // Bogdan's month-one profit is still there and has compounded with
        // month two's; nothing reset it.
        afterSecond.Find(bogdan)!.Distributable
            .Should().BeGreaterThan(afterSecond.Find(andrei)!.Distributable!.Value);
        afterSecond.Find(bogdan)!.CapitalBase.Should().Be(1_000m, "distributions never touch the capital base");
    }

    [Fact]
    public async Task Close_CashAboveDistributable_IsRejected()
    {
        (PoolHarness harness, Guid andrei, _) = await TwoFriendsAtParAsync();

        Result<CloseDistributionResponse> result = await harness.CloseAsync(
            3_520m,
            [new DistributionPayout(andrei, 150m)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.distribution_exceeds_distributable");
    }

    [Fact]
    public async Task Close_DefaultsToEveryNonOwnerWithProfit_AndNeverTheOwner()
    {
        (PoolHarness harness, Guid andrei, Guid bogdan) = await TwoFriendsAtParAsync();

        Result<CloseDistributionResponse> close = await harness.CloseAsync(3_520m);

        close.Value.Lines.Select(l => l.ParticipantId)
            .Should().BeEquivalentTo([andrei, bogdan])
            .And.NotContain(harness.OwnerId);

        // The owner's profit STAYS IN and grows their share; they take value out
        // with a redemption to Bybit when they actually want it.
        PoolSnapshot after = await harness.SnapshotAsync();
        after.Find(harness.OwnerId)!.Stake.Should().Be(1_320m);
    }

    /// <summary>
    /// Re-prices the pool at <paramref name="poolValue"/> without writing
    /// anything — the register is pure, so a hypothetical value can be folded
    /// straight in.
    /// </summary>
    private static async Task<PoolSnapshot> SnapshotAtAsync(PoolHarness harness, decimal poolValue)
    {
        List<PoolParticipant> participants = await harness.Db.PoolParticipants
            .Where(p => p.PoolId == harness.PoolId)
            .ToListAsync();

        List<PoolUnitEvent> events = await harness.Db.PoolUnitEvents
            .Where(e => e.PoolId == harness.PoolId)
            .ToListAsync();

        return PoolUnitRegister.Create(participants, events).SnapshotAsOf(harness.Today, poolValue);
    }
}
