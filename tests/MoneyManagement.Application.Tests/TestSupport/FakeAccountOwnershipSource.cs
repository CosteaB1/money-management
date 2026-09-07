using MoneyManagement.Application.Abstractions.NetWorth;
using NSubstitute;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IAccountOwnershipSource"/>. Mirrors
/// <see cref="FakeExternalClaimSource"/>: <see cref="Empty"/> is the
/// "everything is wholly the user's" baseline every net-worth test was written
/// against, and <see cref="With"/> hand-builds ownership curves that no producer
/// exists to generate yet (the pool entities land in a later commit).
/// </summary>
internal static class FakeAccountOwnershipSource
{
    /// <summary>
    /// A registered source that reports nothing — deliberately distinct from
    /// registering NO source, so tests can prove the two behave identically.
    /// </summary>
    public static IAccountOwnershipSource Empty() => With();

    public static IAccountOwnershipSource With(params AccountOwnership[] ownerships)
    {
        IAccountOwnershipSource source = Substitute.For<IAccountOwnershipSource>();
        source.GetHistoryAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AccountOwnership>>(ownerships));
        return source;
    }

    /// <summary>
    /// One account, one flat fraction that has applied since the beginning of
    /// time — the shortest way to say "half of this account is somebody else's".
    /// </summary>
    public static IAccountOwnershipSource Flat(Guid accountId, decimal fraction) =>
        With(new AccountOwnership(accountId, [new OwnedFractionPoint(DateOnly.MinValue, fraction)]));
}
