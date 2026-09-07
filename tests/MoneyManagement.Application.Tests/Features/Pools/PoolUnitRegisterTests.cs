using FluentAssertions;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The fold itself, tested in isolation from the handlers: units, capital base,
/// NAV, and the unpaid-distribution subtraction that stops the friends being
/// paid twice on the same money.
/// </summary>
public class PoolUnitRegisterTests
{
    private const string Currency = "USD";

    private static readonly DateTime FixedNow = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly Inception = new(2026, 9, 1);
    private static readonly Guid PoolId = Guid.CreateVersion7();

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static PoolParticipant Participant(string name, bool isOwner) =>
        PoolParticipant.Create(PoolId, name, isOwner, Inception, Inception, Clock()).Value;

    private static PoolUnitEvent Event(
        PoolParticipant participant,
        PoolUnitEventKind kind,
        decimal units,
        decimal nav,
        decimal? cash = null,
        DateOnly? occurredOn = null,
        DateOnly? settledOn = null) =>
        PoolUnitEvent.Create(
            PoolId,
            participant.Id,
            kind,
            occurredOn ?? Inception,
            units,
            nav,
            kind == PoolUnitEventKind.Seed ? 0m : 1_000m,
            cash is decimal amount ? new Money(amount, Currency) : null,
            settledOn,
            notes: null,
            Currency,
            Inception,
            participant.IsOwner,
            Clock()).Value;

    [Fact]
    public void SnapshotAsOf_SumsUnitsPerParticipant_AndTotals()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [
                Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m),
                Event(andrei, PoolUnitEventKind.Subscription, 500m, 1m, cash: 500m, settledOn: Inception),
            ]);

        PoolSnapshot snapshot = register.SnapshotAsOf(Today, accountBalance: 1_500m);

        snapshot.TotalUnits.Should().Be(1_500m);
        snapshot.Find(owner.Id)!.Units.Should().Be(1_000m);
        snapshot.Find(andrei.Id)!.Units.Should().Be(500m);
        snapshot.NavPerUnit.Should().Be(1m);
    }

    [Fact]
    public void SnapshotAsOf_IgnoresEventsAfterTheAsOfDate_EndOfDayCutoffIsInclusive()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        var later = new DateOnly(2026, 9, 20);

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [
                Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m),
                Event(andrei, PoolUnitEventKind.Subscription, 500m, 1m, cash: 500m, occurredOn: later, settledOn: later),
            ]);

        // The day before: the subscription has not happened.
        register.SnapshotAsOf(later.AddDays(-1), 1_000m).TotalUnits.Should().Be(1_000m);

        // ON the day: it counts. The value and the units MUST share one cutoff
        // or a month-end subscription inflates the owner's share for one point.
        register.SnapshotAsOf(later, 1_500m).TotalUnits.Should().Be(1_500m);
    }

    [Fact]
    public void SnapshotAsOf_CapitalBase_CountsSubscriptionsMinusRedemptions_Only()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [
                Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m),
                Event(andrei, PoolUnitEventKind.Subscription, 1_000m, 1m, cash: 1_000m, settledOn: Inception),
                Event(andrei, PoolUnitEventKind.Redemption, 200m, 1m, cash: 200m, settledOn: Inception),
                // Neither of these may move the base — that omission IS the
                // high-water mark.
                Event(andrei, PoolUnitEventKind.Distribution, 100m, 1m, cash: 100m, settledOn: Inception),
                Event(andrei, PoolUnitEventKind.CostShare, 10m, 1m),
            ]);

        PoolSnapshot snapshot = register.SnapshotAsOf(Today, accountBalance: 2_000m);

        snapshot.Find(andrei.Id)!.CapitalBase.Should().Be(800m);
    }

    [Fact]
    public void SnapshotAsOf_UnpaidDistribution_IsSubtractedFromPoolValue()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [
                Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m),
                Event(andrei, PoolUnitEventKind.Subscription, 1_000m, 1m, cash: 1_000m, settledOn: Inception),
                // Closed, not yet paid: the cash is still physically in the
                // account, so it must not be counted as pool value again.
                Event(andrei, PoolUnitEventKind.Distribution, 100m, 1m, cash: 100m, occurredOn: Today),
            ]);

        PoolSnapshot snapshot = register.SnapshotAsOf(Today, accountBalance: 2_000m);

        snapshot.UnpaidDistributionCash.Should().Be(100m);
        snapshot.PoolValue.Should().Be(1_900m);
        snapshot.TotalUnits.Should().Be(1_900m);
        snapshot.NavPerUnit.Should().Be(1m);
    }

    [Fact]
    public void SnapshotAsOf_DistributionSettledLater_CountsAsUnpaidAtTheEarlierDate()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        // Both within the fixed clock's "today": PoolUnitEvent refuses future
        // settlement dates, and this test is about the gap between the two, not
        // about the calendar.
        var close = new DateOnly(2026, 9, 20);
        var paid = new DateOnly(2026, 9, 25);

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [
                Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m),
                Event(andrei, PoolUnitEventKind.Subscription, 1_000m, 1m, cash: 1_000m, settledOn: Inception),
                Event(andrei, PoolUnitEventKind.Distribution, 100m, 1m, cash: 100m, occurredOn: close, settledOn: paid),
            ]);

        // At the close the money was still in the account.
        register.SnapshotAsOf(close, 2_000m).UnpaidDistributionCash.Should().Be(100m);

        // After it left, the balance is lower and there is nothing to subtract.
        register.SnapshotAsOf(paid, 1_900m).UnpaidDistributionCash.Should().Be(0m);
        register.SnapshotAsOf(paid, 1_900m).PoolValue.Should().Be(1_900m);
    }

    [Fact]
    public void SnapshotAsOf_NoUnits_LeavesNavUndefined_AndRequireNavFails()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);

        var register = PoolUnitRegister.Create([owner], []);

        PoolSnapshot snapshot = register.SnapshotAsOf(Today, accountBalance: 1_000m);

        snapshot.TotalUnits.Should().Be(0m);
        snapshot.NavPerUnit.Should().BeNull();
        snapshot.Find(owner.Id)!.Stake.Should().BeNull();
        snapshot.Find(owner.Id)!.OwnedFraction.Should().Be(0m);

        Result<decimal> nav = snapshot.RequireNav();
        nav.IsFailure.Should().BeTrue();
        nav.Error.Code.Should().Be("pools.nav_undefined");
    }

    [Fact]
    public void SnapshotAsOf_Distributable_IsZeroBelowBasis_AndProfitAboveIt()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [
                Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m),
                Event(andrei, PoolUnitEventKind.Subscription, 1_000m, 1m, cash: 1_000m, settledOn: Inception),
            ]);

        // Down 10%: their 1,000 units are worth 900 against a 1,000 base.
        PoolSnapshot down = register.SnapshotAsOf(Today, accountBalance: 1_800m);
        down.Find(andrei.Id)!.Stake.Should().Be(900m);
        down.Find(andrei.Id)!.Distributable.Should().Be(0m);

        // Up 10%: only the 100 above basis is payable.
        PoolSnapshot up = register.SnapshotAsOf(Today, accountBalance: 2_200m);
        up.Find(andrei.Id)!.Stake.Should().Be(1_100m);
        up.Find(andrei.Id)!.Distributable.Should().Be(100m);
    }

    [Fact]
    public void SnapshotAsOf_ArchivedParticipants_StayInTheTotal()
    {
        PoolParticipant owner = Participant("Me", isOwner: true);
        PoolParticipant andrei = Participant("Andrei", isOwner: false);

        PoolUnitEvent subscription =
            Event(andrei, PoolUnitEventKind.Subscription, 500m, 1m, cash: 500m, settledOn: Inception);

        // Archiving with units held is refused by the domain, but the register
        // must not depend on that: dropping an archived row from the sum would
        // silently hand their stake to the owner.
        andrei.Archive(0m).IsSuccess.Should().BeTrue();

        var register = PoolUnitRegister.Create(
            [owner, andrei],
            [Event(owner, PoolUnitEventKind.Seed, 1_000m, 1m), subscription]);

        PoolSnapshot snapshot = register.SnapshotAsOf(Today, 1_500m);

        snapshot.TotalUnits.Should().Be(1_500m);
        snapshot.Find(andrei.Id)!.IsArchived.Should().BeTrue();
        snapshot.Find(andrei.Id)!.Units.Should().Be(500m);
    }

    [Fact]
    public void SnapshotAsOf_OrdersPositionsOwnerFirst()
    {
        PoolParticipant andrei = Participant("Andrei", isOwner: false);
        PoolParticipant owner = Participant("Me", isOwner: true);

        var register = PoolUnitRegister.Create([andrei, owner], []);

        register.SnapshotAsOf(Today, 0m).Positions[0].IsOwner.Should().BeTrue();
    }

    [Fact]
    public void RoundUnits_UsesTheStorageScale_SoTheEntityAndTheRowAgree()
    {
        // numeric(28,12): anything the register hands to PoolUnitEvent.Create
        // must already be at this scale, or Postgres rounds a value the
        // application already validated.
        PoolUnitRegister.RoundUnits(1m / 3m).Should().Be(0.333333333333m);
        PoolUnitRegister.RoundMoney(1.005m).Should().Be(1.01m);

        // Distributable floors so the default payout can never exceed what is owed.
        PoolUnitRegister.FloorMoney(1.009m).Should().Be(1.00m);
    }
}
