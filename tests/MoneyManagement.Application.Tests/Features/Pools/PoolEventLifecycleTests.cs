using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.EventHandlers;
using MoneyManagement.Application.Features.Pools.RecordCostReimbursement;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// Undoing things: the sanctioned event delete, and the inverse coupling when a
/// transaction is soft-deleted from under a unit event.
/// </summary>
public class PoolEventLifecycleTests
{
    private static async Task<(PoolHarness Harness, Guid Andrei)> PoolWithOneFriendAsync(
        params Account[] extraAccounts)
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m, extraAccounts: extraAccounts);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_200m, 800m);
        subscription.IsSuccess.Should().BeTrue(subscription.IsFailure ? subscription.Error.Code : null);

        return (harness, andrei);
    }

    [Fact]
    public async Task DeleteEvent_RemovesTheUnits_AndSoftDeletesTheMoneyRow()
    {
        (PoolHarness harness, Guid andrei) = await PoolWithOneFriendAsync();

        PoolUnitEvent subscription = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Kind == PoolUnitEventKind.Subscription);

        Guid legId = subscription.MovementTransactionId!.Value;

        Result result = await harness.DeleteEventAsync(subscription.Id);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        (await harness.Db.PoolUnitEvents.ToListAsync())
            .Should().NotContain(e => e.Id == subscription.Id);

        Transaction leg = (await harness.Db.Transactions.ToListAsync()).Single(t => t.Id == legId);
        leg.IsDeleted.Should().BeTrue();

        // Money and units came off together — the point of routing deletes
        // through the pool rather than the transactions page.
        (await harness.DerivedBalanceAsync()).Should().Be(1_200m);
        (await harness.SnapshotAsync()).Find(andrei)!.Units.Should().Be(0m);
    }

    [Fact]
    public async Task DeleteEvent_OfATwoLegRedemption_AlsoRemovesTheCounterLeg()
    {
        Account bybit = Account.Create(
            "Bybit",
            AccountType.CryptoExchange,
            new Money(0m, PoolHarness.Currency),
            new DateOnly(2026, 1, 1),
            notes: null).Value;

        (PoolHarness harness, _) = await PoolWithOneFriendAsync(bybit);

        Result<RecordRedemptionResponse> redemption =
            await harness.RedeemAsync(harness.OwnerId, 2_000m, 400m, destinationAccountId: bybit.Id);

        redemption.IsSuccess.Should().BeTrue(redemption.IsFailure ? redemption.Error.Code : null);

        PoolUnitEvent unitEvent = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Kind == PoolUnitEventKind.Redemption);

        (await harness.DeleteEventAsync(unitEvent.Id)).IsSuccess.Should().BeTrue();

        List<Transaction> rows = await harness.Db.Transactions.ToListAsync();

        // Both halves go. Removing only the pool side would leave Bybit
        // permanently credited with money that never arrived.
        rows.Single(t => t.Id == redemption.Value.MovementTransactionId).IsDeleted.Should().BeTrue();
        rows.Single(t => t.Id == redemption.Value.CounterTransactionId).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEvent_OfTheSeed_IsRejected()
    {
        (PoolHarness harness, _) = await PoolWithOneFriendAsync();

        PoolUnitEvent seed = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Kind == PoolUnitEventKind.Seed);

        Result result = await harness.DeleteEventAsync(seed.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.seed_cannot_be_deleted");
    }

    [Fact]
    public async Task DeleteEvent_OfHalfACostTransfer_RemovesTheWholeBatch_SoUnitsStayBalanced()
    {
        (PoolHarness harness, _) = await PoolWithOneFriendAsync();

        Result<RecordCostReimbursementResponse> cost = await harness.ReimburseCostAsync(100m);
        cost.IsSuccess.Should().BeTrue(cost.IsFailure ? cost.Error.Code : null);

        PoolSnapshot before = await harness.SnapshotAsync();

        // Delete only the participant's half.
        Result result = await harness.DeleteEventAsync(cost.Value.Lines.Single().EventId);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        List<PoolUnitEvent> remaining = await harness.Db.PoolUnitEvents.ToListAsync();
        remaining.Should().NotContain(e =>
            e.Kind == PoolUnitEventKind.CostShare || e.Kind == PoolUnitEventKind.CostRecovery);

        // Total units are unchanged by the undo, as they were by the transfer.
        PoolSnapshot after = await harness.SnapshotAsync();
        after.TotalUnits.Should().Be(before.TotalUnits);
        after.NavPerUnit.Should().Be(before.NavPerUnit);
    }

    [Fact]
    public async Task DeleteEvent_UnderTheWrongPool_IsNotFound()
    {
        (PoolHarness harness, _) = await PoolWithOneFriendAsync();

        Result result = await harness.DeleteEventAsync(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.unit_event_not_found");
    }

    [Fact]
    public async Task TransactionDeleted_ClearsTheLink_ButKeepsTheUnitEvent()
    {
        (PoolHarness harness, Guid andrei) = await PoolWithOneFriendAsync();

        PoolUnitEvent subscription = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Kind == PoolUnitEventKind.Subscription);

        decimal unitsBefore = subscription.Units;

        await HandleDeletedAsync(harness, subscription.MovementTransactionId!.Value);

        PoolUnitEvent after = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Id == subscription.Id);

        // The ledger row SURVIVES — unlike a loan payment. The units really did
        // move; silently un-issuing them would shift a third party's share with
        // no audit trail.
        after.MovementTransactionId.Should().BeNull();
        after.Units.Should().Be(unitsBefore);
        (await harness.SnapshotAsync()).Find(andrei)!.Units.Should().Be(unitsBefore);
    }

    [Fact]
    public async Task TransactionDeleted_OnASettledDistribution_MakesItUnpaidAgain()
    {
        (PoolHarness harness, Guid andrei) = await PoolWithOneFriendAsync();

        Result<CloseDistributionResponse> close =
            await harness.CloseAsync(2_200m, [new DistributionPayout(andrei)]);

        close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.Code : null);
        Guid eventId = close.Value.Lines.Single().EventId;

        harness.Advance(2);
        Result<SettleDistributionResponse> settle = await harness.SettleAsync(eventId);
        settle.IsSuccess.Should().BeTrue(settle.IsFailure ? settle.Error.Code : null);

        // Now the payment row is deleted from the transactions page: the cash is
        // back in the account, so the payout is owed again.
        await HandleDeletedAsync(harness, settle.Value.TransactionId);

        PoolUnitEvent after = (await harness.Db.PoolUnitEvents.ToListAsync()).Single(e => e.Id == eventId);

        after.SettledOn.Should().BeNull();
        after.MovementTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task TransactionDeleted_ForANonTransferRow_IsAFastNoOp()
    {
        (PoolHarness harness, _) = await PoolWithOneFriendAsync();

        PoolUnitEvent subscription = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Kind == PoolUnitEventKind.Subscription);

        // The re-pricing mark is IsAdjustment / not a transfer, and no unit event
        // links it. The handler must not touch anything.
        var handler = new ClearPoolMovementOnTransactionDeletedHandler(
            harness.Db,
            NullLogger<ClearPoolMovementOnTransactionDeletedHandler>.Instance);

        await handler.Handle(
            new TransactionDeletedDomainEvent(
                subscription.MovementTransactionId!.Value,
                CategoryId: null,
                harness.Today,
                AmountMdl: null,
                TransactionDirection.Income,
                IsTransfer: false,
                IsAdjustment: true),
            CancellationToken.None);

        (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Id == subscription.Id)
            .MovementTransactionId.Should().NotBeNull();
    }

    private static async Task HandleDeletedAsync(PoolHarness harness, Guid transactionId)
    {
        Transaction transaction = (await harness.Db.Transactions.ToListAsync())
            .Single(t => t.Id == transactionId);

        var handler = new ClearPoolMovementOnTransactionDeletedHandler(
            harness.Db,
            NullLogger<ClearPoolMovementOnTransactionDeletedHandler>.Instance);

        await handler.Handle(
            new TransactionDeletedDomainEvent(
                transactionId,
                transaction.CategoryId,
                transaction.TransactionDate,
                AmountMdl: null,
                transaction.Direction,
                transaction.IsTransfer,
                transaction.IsAdjustment),
            CancellationToken.None);
    }
}
