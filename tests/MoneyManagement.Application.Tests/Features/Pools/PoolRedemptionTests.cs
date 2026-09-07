using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The two-leg / one-leg split. Choosing wrong on a live transfer silently
/// deletes real net worth, so both paths are pinned.
/// </summary>
public class PoolRedemptionTests
{
    private static Account Bybit(string currency = PoolHarness.Currency) =>
        Account.Create(
            "Bybit",
            AccountType.CryptoExchange,
            new Money(0m, currency),
            new DateOnly(2026, 1, 1),
            notes: null).Value;

    private static async Task<PoolHarness> SeededAsync(params Account[] extraAccounts)
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m, extraAccounts: extraAccounts);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);
        return harness;
    }

    [Fact]
    public async Task Redemption_WithDestinationAccount_WritesTwoReciprocalLegs()
    {
        Account bybit = Bybit();
        PoolHarness harness = await SeededAsync(bybit);

        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, 1_200m, 400m, destinationAccountId: bybit.Id);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.CounterTransactionId.Should().NotBeNull();

        List<Transaction> rows = await harness.Db.Transactions.ToListAsync();

        Transaction poolLeg = rows.Single(t => t.Id == result.Value.MovementTransactionId);
        poolLeg.AccountId.Should().Be(harness.Account.Id);
        poolLeg.Direction.Should().Be(TransactionDirection.Expense);
        poolLeg.CounterAccountId.Should().Be(bybit.Id);
        poolLeg.IsTransfer.Should().BeTrue();
        poolLeg.CategoryId.Should().Be(SeededCategories.PoolId);

        Transaction counterLeg = rows.Single(t => t.Id == result.Value.CounterTransactionId);
        counterLeg.AccountId.Should().Be(bybit.Id);
        counterLeg.Direction.Should().Be(TransactionDirection.Income);
        counterLeg.CounterAccountId.Should().Be(harness.Account.Id);
        counterLeg.Amount.Amount.Should().Be(400m);

        // Net worth is conserved: 400 leaves the pool and 400 lands on Bybit.
        // The one-leg path here would have destroyed 400 of real net worth.
        (await harness.DerivedBalanceAsync()).Should().Be(800m);
    }

    [Fact]
    public async Task Redemption_WithoutDestinationAccount_WritesASingleLeg()
    {
        PoolHarness harness = await SeededAsync();

        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, 1_200m, 400m);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.CounterTransactionId.Should().BeNull();

        List<Transaction> rows = await harness.Db.Transactions.ToListAsync();
        Transaction leg = rows.Single(t => t.Id == result.Value.MovementTransactionId);

        leg.CounterAccountId.Should().BeNull();
        leg.Direction.Should().Be(TransactionDirection.Expense);
        (await harness.DerivedBalanceAsync()).Should().Be(800m);
    }

    [Fact]
    public async Task Redemption_ToADifferentCurrencyAccount_IsRejected()
    {
        Account eurAccount = Bybit("EUR");
        PoolHarness harness = await SeededAsync(eurAccount);

        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, 1_200m, 400m, destinationAccountId: eurAccount.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.destination_currency_mismatch");
    }

    [Fact]
    public async Task Redemption_ToThePoolsOwnAccount_IsRejected()
    {
        PoolHarness harness = await SeededAsync();

        Result<RecordRedemptionResponse> result = await harness.RedeemAsync(
            harness.OwnerId,
            1_200m,
            400m,
            destinationAccountId: harness.Account.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("transfer.same_source_and_destination");
    }

    [Fact]
    public async Task Redemption_BeyondTheParticipantsUnits_IsRejected()
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_200m, 500m);
        subscription.IsSuccess.Should().BeTrue(subscription.IsFailure ? subscription.Error.Code : null);

        // Andrei holds 500 units; asking for 900 would drive him negative and
        // push somebody else's fraction above 1.0.
        Result<RecordRedemptionResponse> result = await harness.RedeemAsync(andrei, 1_700m, 900m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.redemption_exceeds_stake");
    }

    [Fact]
    public async Task Redemption_OfTheParticipantsWholeStake_IsAllowed_AndLeavesThemAtZero()
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        await harness.SubscribeAsync(andrei, 1_200m, 500m);

        Result<RecordRedemptionResponse> result = await harness.RedeemAsync(andrei, 1_700m, 500m);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        (await harness.SnapshotAsync()).Find(andrei)!.Units.Should().Be(0m);
    }

    // ---- winding the whole pool down -------------------------------------

    [Fact]
    public async Task WindingTheWholePoolDown_WithoutTheAcknowledgement_IsBlocked()
    {
        PoolHarness harness = await SeededAsync();

        // Redeeming the LAST of a pool is by definition redeeming the whole pool
        // value, so it collides head-on with the guard against a BALANCE typed
        // into an AMOUNT field. Left alone, that makes a wind-down unreachable:
        // partial redemptions converge on zero units and never arrive.
        Result<RecordRedemptionResponse> result = await harness.RedeemAsync(harness.OwnerId, 1_200m, 1_200m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cash_looks_like_a_balance");
    }

    [Fact]
    public async Task WindingTheWholePoolDown_WithTheAcknowledgement_IsAllowed()
    {
        PoolHarness harness = await SeededAsync();

        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, 1_200m, 1_200m, isFullWindDown: true);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Units.Should().Be(1_200m, "at a NAV of 1 the whole 1,200 of value is 1,200 units");

        PoolSnapshot snapshot = await harness.SnapshotAsync();
        snapshot.TotalUnits.Should().Be(0m);
        snapshot.NavPerUnit.Should().BeNull("no units outstanding means no price");
        (await harness.DerivedBalanceAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task TheAcknowledgement_OnARedemptionThatLeavesUnitsOutstanding_IsRejected()
    {
        PoolHarness harness = await SeededAsync();

        // THE FLAG IS AN ASSERTION, NOT A BYPASS SWITCH. Claiming a full wind-down
        // while 800 of value stays in the pool is refused — otherwise the typo the
        // balance guard exists to catch would be one checkbox away from landing
        // again.
        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, 1_200m, 400m, isFullWindDown: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.not_a_full_wind_down");
    }

    [Fact]
    public async Task WindingDownAFriendsWholeStake_StillLeavesTheOwnersUnits()
    {
        PoolHarness harness = await SeededAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_200m, 500m);
        subscription.IsSuccess.Should().BeTrue(subscription.IsFailure ? subscription.Error.Code : null);

        // A friend's full exit is not a wind-down: the owner's units keep the pool
        // alive, so the flag is false even though Andrei ends at zero.
        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(andrei, 1_700m, 500m, isFullWindDown: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.not_a_full_wind_down");
    }

    // ---- the destination account is the OWNER's path only -----------------

    [Fact]
    public async Task Redemption_ByANonOwner_CannotNameADestinationAccount()
    {
        Account bybit = Bybit();
        PoolHarness harness = await SeededAsync(bybit);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_200m, 500m);
        subscription.IsSuccess.Should().BeTrue(subscription.IsFailure ? subscription.Error.Code : null);

        // The pool-side leg is correctly excluded from the owner's figures by the
        // ownership fraction — but the COUNTER leg lands on a wholly-owned
        // account, where it reads as the user's own contribution and its balance
        // counts in full towards net worth. Andrei's money would become the
        // user's on the way out.
        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(andrei, 1_700m, 400m, destinationAccountId: bybit.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.destination_requires_owner");

        // …and the one-leg form of the same payout is fine: the money genuinely
        // leaves the tracked world.
        Result<RecordRedemptionResponse> oneLeg = await harness.RedeemAsync(andrei, 1_700m, 400m);
        oneLeg.IsSuccess.Should().BeTrue(oneLeg.IsFailure ? oneLeg.Error.Code : null);
        oneLeg.Value.CounterTransactionId.Should().BeNull();
    }
}
