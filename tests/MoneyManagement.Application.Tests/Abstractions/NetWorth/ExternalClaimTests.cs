using FluentAssertions;
using MoneyManagement.Application.Abstractions.NetWorth;

namespace MoneyManagement.Application.Tests.Abstractions.NetWorth;

/// <summary>
/// The as-of arithmetic every net-worth caller leans on. Pinned here so the
/// date boundaries (inclusive on both ends) can't drift under a refactor of the
/// handlers that consume it.
/// </summary>
public sealed class ExternalClaimTests
{
    private static ExternalClaim Claim(
        decimal amount,
        DateOnly effectiveFrom,
        params ExternalClaimSettlement[] settlements) =>
        new(amount, "MDL", effectiveFrom, ExternalClaimSide.ReducesNetWorth, settlements);

    [Fact]
    public void OutstandingAsOf_BeforeEffectiveFrom_IsZero()
    {
        ExternalClaim claim = Claim(1_000m, new DateOnly(2026, 3, 10));

        claim.OutstandingAsOf(new DateOnly(2026, 3, 9)).Should().Be(0m);
    }

    [Fact]
    public void OutstandingAsOf_OnEffectiveFrom_IsFullAmount()
    {
        ExternalClaim claim = Claim(1_000m, new DateOnly(2026, 3, 10));

        claim.OutstandingAsOf(new DateOnly(2026, 3, 10)).Should().Be(1_000m);
    }

    [Fact]
    public void OutstandingAsOf_CountsSettlementsOnTheAsOfDate()
    {
        ExternalClaim claim = Claim(
            1_000m,
            new DateOnly(2026, 3, 10),
            new ExternalClaimSettlement(400m, new DateOnly(2026, 4, 1)));

        claim.OutstandingAsOf(new DateOnly(2026, 4, 1)).Should().Be(600m);
    }

    [Fact]
    public void OutstandingAsOf_IgnoresSettlementsAfterTheAsOfDate()
    {
        ExternalClaim claim = Claim(
            1_000m,
            new DateOnly(2026, 3, 10),
            new ExternalClaimSettlement(400m, new DateOnly(2026, 4, 2)));

        claim.OutstandingAsOf(new DateOnly(2026, 4, 1)).Should().Be(1_000m);
    }

    [Fact]
    public void OutstandingAsOf_OverSettled_GoesNegativeRatherThanClamping()
    {
        // Callers skip non-positive claims; inferring a reverse obligation from
        // an overpayment is a product decision nobody has made.
        ExternalClaim claim = Claim(
            1_000m,
            new DateOnly(2026, 3, 10),
            new ExternalClaimSettlement(1_250m, new DateOnly(2026, 4, 1)));

        claim.OutstandingAsOf(new DateOnly(2026, 4, 1)).Should().Be(-250m);
    }
}
