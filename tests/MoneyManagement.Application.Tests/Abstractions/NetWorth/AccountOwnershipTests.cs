using FluentAssertions;
using MoneyManagement.Application.Abstractions.NetWorth;

namespace MoneyManagement.Application.Tests.Abstractions.NetWorth;

/// <summary>
/// The as-of arithmetic for the ownership seam. Pinned here for the same reason
/// <see cref="ExternalClaimTests"/> exists: the date boundary is shared with
/// <c>AccountBalanceLedger</c> and a one-day drift between the two would show up
/// as a phantom spike on exactly one trend point.
/// </summary>
public sealed class AccountOwnershipTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();

    private static AccountOwnership Ownership(params OwnedFractionPoint[] points) =>
        new(AccountId, points);

    [Fact]
    public void OwnedFractionAsOf_NoPoints_IsWhollyOwned()
    {
        // "No ownership data" means nobody else has money in here — never
        // "the user owns nothing".
        Ownership().OwnedFractionAsOf(new DateOnly(2026, 5, 1)).Should().Be(1m);
    }

    [Fact]
    public void OwnedFractionAsOf_BeforeTheFirstPoint_IsWhollyOwned()
    {
        AccountOwnership ownership = Ownership(new OwnedFractionPoint(new DateOnly(2026, 3, 10), 0.5m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 3, 9)).Should().Be(1m);
    }

    [Fact]
    public void OwnedFractionAsOf_OnTheEffectiveDate_Applies()
    {
        // END-OF-DAY cutoff, matching AccountBalanceLedger's `date <= asOf`.
        // A subscription that lands on a month-end also raises the balance that
        // day, so the fraction has to bite on the same day or the owner's share
        // is overstated for exactly one trend point.
        AccountOwnership ownership = Ownership(new OwnedFractionPoint(new DateOnly(2026, 3, 31), 0.5m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 3, 31)).Should().Be(0.5m);
    }

    [Fact]
    public void OwnedFractionAsOf_LastPointOnOrBeforeAsOf_Wins()
    {
        AccountOwnership ownership = Ownership(
            new OwnedFractionPoint(new DateOnly(2026, 1, 1), 0.8m),
            new OwnedFractionPoint(new DateOnly(2026, 3, 1), 0.6m),
            new OwnedFractionPoint(new DateOnly(2026, 5, 1), 0.4m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 4, 30)).Should().Be(0.6m);
        ownership.OwnedFractionAsOf(new DateOnly(2026, 5, 1)).Should().Be(0.4m);
        ownership.OwnedFractionAsOf(new DateOnly(2030, 1, 1)).Should().Be(
            0.4m,
            "the newest point keeps applying forever forward");
    }

    [Fact]
    public void OwnedFractionAsOf_FutureOnlyPoints_AreIgnored()
    {
        AccountOwnership ownership = Ownership(
            new OwnedFractionPoint(new DateOnly(2026, 6, 1), 0.5m),
            new OwnedFractionPoint(new DateOnly(2026, 7, 1), 0.25m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 5, 31)).Should().Be(1m);
    }

    [Fact]
    public void OwnedFractionAsOf_UnsortedPoints_StopAtTheFirstFutureOne()
    {
        // NOT a defensive-sorting test: oldest-first is the PRODUCER's contract
        // (see AccountOwnership.Points). This pins the documented consequence of
        // breaking it, so nobody "fixes" the consumer by sorting on every one of
        // the 24 as-of evaluations a single trend request performs.
        AccountOwnership ownership = Ownership(
            new OwnedFractionPoint(new DateOnly(2026, 1, 1), 0.9m),
            new OwnedFractionPoint(new DateOnly(2026, 9, 1), 0.1m),
            new OwnedFractionPoint(new DateOnly(2026, 2, 1), 0.5m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 5, 1)).Should().Be(
            0.9m,
            "the loop breaks at the out-of-order future point and never sees the Feb one");
    }

    [Fact]
    public void OwnedFractionAsOf_ZeroFraction_IsHonoured()
    {
        // The whole balance is somebody else's. Distinct from "no data" — which
        // is why the default is 1m rather than a nullable.
        AccountOwnership ownership = Ownership(new OwnedFractionPoint(new DateOnly(2026, 1, 1), 0m));

        ownership.OwnedFractionAsOf(new DateOnly(2026, 5, 1)).Should().Be(0m);
    }
}
