using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.RecordCostReimbursement;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The pure unit transfer. If any of these start failing, a server bill has
/// begun creating or destroying pool value.
/// </summary>
public class PoolCostReimbursementTests
{
    /// <summary>Owner 1,200 units, Andrei 400, Bogdan 400 — total 2,000, all at NAV 1.0.</summary>
    private static async Task<(PoolHarness Harness, Guid Andrei, Guid Bogdan)> PoolWithTwoFriendsAsync()
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_200m, 400m)).IsSuccess.Should().BeTrue();

        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        (await harness.SubscribeAsync(bogdan, 1_600m, 400m)).IsSuccess.Should().BeTrue();

        return (harness, andrei, bogdan);
    }

    [Fact]
    public async Task CostReimbursement_LeavesTotalUnitsAndNavUnchanged()
    {
        (PoolHarness harness, Guid andrei, Guid bogdan) = await PoolWithTwoFriendsAsync();

        PoolSnapshot before = await harness.SnapshotAsync();
        before.TotalUnits.Should().Be(2_000m);
        before.NavPerUnit.Should().Be(1m);

        // A USD 100 server bill the owner paid from their own pocket.
        Result<RecordCostReimbursementResponse> result = await harness.ReimburseCostAsync(100m);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        PoolSnapshot after = await harness.SnapshotAsync();

        // THE INVARIANT: units and value both unmoved, so NAV is too. Only who
        // owns the pool changed.
        after.TotalUnits.Should().Be(before.TotalUnits);
        after.PoolValue.Should().Be(before.PoolValue);
        after.NavPerUnit.Should().Be(before.NavPerUnit);

        // Each friend holds 400/2,000 = 20% of the pool, so each bears 20 of the
        // 100. The owner recovers the 40 that was not theirs to bear — not the
        // whole 100.
        after.Find(andrei)!.Units.Should().Be(380m);
        after.Find(bogdan)!.Units.Should().Be(380m);
        after.Find(harness.OwnerId)!.Units.Should().Be(1_240m);

        result.Value.TotalUnitsTransferred.Should().Be(40m);
        result.Value.TotalAmountRecovered.Should().Be(40m);
        result.Value.Lines.Should().HaveCount(2);
        result.Value.Lines.Should().AllSatisfy(l => l.Amount.Should().Be(20m));
    }

    [Fact]
    public async Task CostReimbursement_WritesNoCashLegAndNoTransaction()
    {
        (PoolHarness harness, _, _) = await PoolWithTwoFriendsAsync();

        int before = (await harness.Db.Transactions.ToListAsync()).Count;

        Result<RecordCostReimbursementResponse> result = await harness.ReimburseCostAsync(100m);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        // The money never entered the account; a cash leg would mint value out
        // of nothing.
        (await harness.Db.Transactions.ToListAsync()).Should().HaveCount(before);
        result.Value.MarkTransactionId.Should().BeNull();
        result.Value.MarkDelta.Should().Be(0m);

        List<PoolUnitEvent> costEvents = await harness.Db.PoolUnitEvents
            .Where(e => e.Kind == PoolUnitEventKind.CostShare || e.Kind == PoolUnitEventKind.CostRecovery)
            .ToListAsync();

        costEvents.Should().HaveCount(3);
        costEvents.Should().AllSatisfy(e =>
        {
            e.Cash.Should().BeNull();
            e.SettledOn.Should().BeNull();
            e.MovementTransactionId.Should().BeNull();
        });
    }

    [Fact]
    public async Task CostReimbursement_LegsBalanceToWithinTheDustTolerance()
    {
        (PoolHarness harness, _, _) = await PoolWithTwoFriendsAsync();

        (await harness.ReimburseCostAsync(37.77m)).IsSuccess.Should().BeTrue();

        List<PoolUnitEvent> costEvents = await harness.Db.PoolUnitEvents
            .Where(e => e.Kind == PoolUnitEventKind.CostShare || e.Kind == PoolUnitEventKind.CostRecovery)
            .ToListAsync();

        decimal net = costEvents.Sum(e => e.UnitsDelta);
        Math.Abs(net).Should().BeLessThanOrEqualTo(PoolUnitEvent.UnitsDustTolerance);
    }

    [Fact]
    public async Task CostReimbursement_WithAPoolValue_MarksFirst_ThenPricesAtTheFreshNav()
    {
        (PoolHarness harness, Guid andrei, _) = await PoolWithTwoFriendsAsync();

        // Pool has run up to 2,400 (NAV 1.2) since the last mark.
        Result<RecordCostReimbursementResponse> result =
            await harness.ReimburseCostAsync(120m, poolValueNow: 2_400m);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.MarkDelta.Should().Be(400m);
        result.Value.NavPerUnit.Should().Be(1.2m);

        Transaction mark = (await harness.Db.Transactions.ToListAsync())
            .Single(t => t.Id == result.Value.MarkTransactionId);
        mark.IsAdjustment.Should().BeTrue();

        // Andrei's 20% of 120 is 24, which at NAV 1.2 is 20 units — fewer than
        // the 24 a stale NAV of 1.0 would have taken off him.
        result.Value.Lines.Single(l => l.ParticipantId == andrei).Amount.Should().Be(24m);
        result.Value.Lines.Single(l => l.ParticipantId == andrei).Units.Should().Be(20m);

        PoolSnapshot after = await harness.SnapshotAsync();
        after.NavPerUnit.Should().Be(1.2m);
        after.TotalUnits.Should().Be(2_000m);
    }

    [Fact]
    public async Task CostReimbursement_WithNoOutsideCapital_IsRejected()
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);

        Result<RecordCostReimbursementResponse> result = await harness.ReimburseCostAsync(100m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cost_no_outside_units");
    }

    [Fact]
    public async Task CostReimbursement_BeyondAParticipantsUnits_IsRejected()
    {
        (PoolHarness harness, _, _) = await PoolWithTwoFriendsAsync();

        // Each friend holds 400 units; a 5,000 bill would take 1,000 off each.
        Result<RecordCostReimbursementResponse> result = await harness.ReimburseCostAsync(5_000m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cost_exceeds_participant_units");
    }
}
