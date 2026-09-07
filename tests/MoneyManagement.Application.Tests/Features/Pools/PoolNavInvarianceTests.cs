using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The property the whole model rests on: subscribing or redeeming AT NAV
/// leaves NAV unchanged — <c>(V ± X) ÷ (U ± X/nav) = nav</c>. If this ever
/// stops holding, every participant is being mispriced on every capital event.
/// </summary>
public class PoolNavInvarianceTests
{
    /// <summary>NAV is stored at numeric(28,12); assert it at exactly that scale.</summary>
    private const int NavScale = 12;

    [Fact]
    public async Task Subscription_LeavesNavUnchanged_To12dp()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        // Pool has run up to 2,000 before the money lands.
        PoolSnapshot before = await harness.SnapshotAsync();
        decimal navBefore = Nav(before);

        Result<RecordSubscriptionResponse> result = await harness.SubscribeAsync(andrei, 2_000m, 700m);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        // Priced at the marked value, not the stale one.
        result.Value.NavPerUnit.Should().Be(decimal.Round(2_000m / 1_092m, NavScale));

        PoolSnapshot after = await harness.SnapshotAsync();
        Nav(after).Should().Be(result.Value.NavPerUnit);

        // The invariance itself: the subscription moved nobody else's per-unit
        // value by a cent.
        Nav(after).Should().NotBe(navBefore, "the pool was re-marked from 1,092 to 2,000 first");
        after.PoolValue.Should().Be(2_700m);
        after.TotalUnits.Should().Be(1_092m + result.Value.Units);

        // And the owner's stake is untouched by the arrival.
        before.Find(harness.OwnerId)!.Units.Should().Be(after.Find(harness.OwnerId)!.Units);
    }

    [Fact]
    public async Task SubscriptionAtTheMarkedValue_KeepsNavExactlyWhereItWas()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        // Mark to 2,175.68 first, so navBefore is real rather than 1.0.
        await harness.SubscribeAsync(andrei, 2_175.68m, 1_000m);

        PoolSnapshot before = await harness.SnapshotAsync();
        decimal navBefore = Nav(before);

        Guid bogdan = await harness.AddParticipantAsync("Bogdan");

        // Second subscription at exactly the value the app already derives: no
        // mark is written and NAV must not budge.
        Result<RecordSubscriptionResponse> result =
            await harness.SubscribeAsync(bogdan, before.PoolValue, 500m);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.MarkDelta.Should().Be(0m);
        result.Value.MarkTransactionId.Should().BeNull();
        result.Value.NavPerUnit.Should().Be(navBefore);

        Nav(await harness.SnapshotAsync()).Should().Be(navBefore);
    }

    [Fact]
    public async Task Redemption_LeavesNavUnchanged_To12dp()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        await harness.SubscribeAsync(andrei, 1_092m, 1_000m);

        // Mark up, then take money out at the resulting NAV.
        PoolSnapshot marked = await harness.SnapshotAsync();
        marked.PoolValue.Should().Be(2_092m);

        Result<RecordRedemptionResponse> result = await harness.RedeemAsync(andrei, 2_300m, 300m);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        decimal navStruck = result.Value.NavPerUnit;
        navStruck.Should().Be(decimal.Round(2_300m / 2_092m, NavScale));

        PoolSnapshot after = await harness.SnapshotAsync();
        Nav(after).Should().Be(navStruck);
        after.PoolValue.Should().Be(2_000m);
    }

    [Fact]
    public async Task OwnerWithdrawalMidPeriod_ChangesNobodyElsesPerUnitValue()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        await harness.SubscribeAsync(andrei, 1_092m, 1_000m);

        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        await harness.SubscribeAsync(bogdan, 2_175.68m, 1_000m);

        PoolSnapshot before = await harness.SnapshotAsync();
        decimal navBefore = Nav(before);
        decimal andreiUnitsBefore = before.Find(andrei)!.Units;
        decimal bogdanUnitsBefore = before.Find(bogdan)!.Units;
        decimal andreiStakeBefore = before.Find(andrei)!.Stake!.Value;
        decimal bogdanStakeBefore = before.Find(bogdan)!.Stake!.Value;

        // The owner moves 400 of their OWN money to Bybit mid-period. Every
        // non-unit model needs a special case for this; units make it a division.
        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, before.PoolValue, 400m);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        PoolSnapshot after = await harness.SnapshotAsync();

        Nav(after).Should().Be(navBefore);
        after.Find(andrei)!.Units.Should().Be(andreiUnitsBefore);
        after.Find(bogdan)!.Units.Should().Be(bogdanUnitsBefore);
        after.Find(andrei)!.Stake.Should().Be(andreiStakeBefore);
        after.Find(bogdan)!.Stake.Should().Be(bogdanStakeBefore);

        // Only the owner was diluted — which is exactly the "Binance only" scope
        // decision made visible.
        after.Find(harness.OwnerId)!.OwnedFraction
            .Should().BeLessThan(before.Find(harness.OwnerId)!.OwnedFraction);
    }

    [Fact]
    public async Task WorkedSeptemberExample_ReproducesTheHandComputedFigures()
    {
        // Mirrors POOLED-CAPITAL.md section 6, so a regression in the pricing
        // arithmetic shows up against numbers that were verified by hand.
        var harness = PoolHarness.Create(openingBalance: 1_050m);

        // Sep 6 — pool created against a real 1,092.00 (the app derived 1,050.00).
        CreatePoolResponse created = await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        created.MarkDelta.Should().Be(42m);
        created.SeedUnits.Should().Be(1_092m);

        // Sep 8 — Andrei subscribes 1,000 at a pre-money of 1,092: NAV is 1.0.
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andreiSub = await harness.SubscribeAsync(andrei, 1_092m, 1_000m);
        andreiSub.Value.NavPerUnit.Should().Be(1m);
        andreiSub.Value.Units.Should().Be(1_000m);
        andreiSub.Value.MarkDelta.Should().Be(0m);

        // Sep 12 — Bogdan subscribes 1,000 against a real 2,175.68: NAV is 1.04.
        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Result<RecordSubscriptionResponse> bogdanSub = await harness.SubscribeAsync(bogdan, 2_175.68m, 1_000m);
        bogdanSub.Value.MarkDelta.Should().Be(83.68m);
        bogdanSub.Value.NavPerUnit.Should().Be(1.04m);
        bogdanSub.Value.Units.Should().Be(961.538461538462m);

        PoolSnapshot afterBogdan = await harness.SnapshotAsync();
        afterBogdan.PoolValue.Should().Be(3_175.68m);
        afterBogdan.TotalUnits.Should().Be(3_053.538461538462m);
        Nav(afterBogdan).Should().Be(1.04m);

        // Sep 18 — 400 to Bybit. NAV survives untouched.
        Result<RecordRedemptionResponse> withdrawal =
            await harness.RedeemAsync(harness.OwnerId, 3_175.68m, 400m);

        withdrawal.Value.NavPerUnit.Should().Be(1.04m);
        withdrawal.Value.Units.Should().Be(384.615384615385m);

        PoolSnapshot afterWithdrawal = await harness.SnapshotAsync();
        afterWithdrawal.PoolValue.Should().Be(2_775.68m);
        Nav(afterWithdrawal).Should().Be(1.04m);

        // Bogdan bought in 4% higher, so he earns less than Andrei on the same
        // 1,000 — the ~USD 21/month a naive capital-weighted split gets wrong.
        afterWithdrawal.Find(andrei)!.Units.Should().BeGreaterThan(afterWithdrawal.Find(bogdan)!.Units);
    }

    [Fact]
    public async Task SubscriptionWritesTheMarkFirst_ThenTheTransferLeg_InOneSave()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_050m);
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        Result<RecordSubscriptionResponse> result = await harness.SubscribeAsync(andrei, 1_100m, 500m);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        List<Transaction> rows = await harness.Db.Transactions.ToListAsync();

        Transaction mark = rows.Single(t => t.Id == result.Value.MarkTransactionId);
        mark.IsAdjustment.Should().BeTrue();
        mark.IsTransfer.Should().BeFalse();
        mark.CategoryId.Should().Be(SeededCategories.BalanceAdjustmentId);
        mark.Description.Should().Be("Balance adjustment");
        mark.Amount.Amount.Should().Be(50m);
        mark.Direction.Should().Be(TransactionDirection.Income);

        Transaction leg = rows.Single(t => t.Id == result.Value.MovementTransactionId);
        leg.IsTransfer.Should().BeTrue();
        leg.IsAdjustment.Should().BeFalse();
        leg.CategoryId.Should().Be(SeededCategories.PoolId);
        leg.Source.Should().Be(TransactionSource.Manual);
        leg.CounterAccountId.Should().BeNull();
        leg.Direction.Should().Be(TransactionDirection.Income);
        leg.Amount.Amount.Should().Be(500m);
        leg.Description.Should().Be("Pool subscription from Andrei");

        // FX-converted at the movement's own date; identity converter for USD→USD
        // would return null, so assert the row simply exists in the account's
        // derived balance instead.
        (await harness.DerivedBalanceAsync()).Should().Be(1_600m);
    }

    private static decimal Nav(PoolSnapshot snapshot) => snapshot.NavPerUnit!.Value;
}
