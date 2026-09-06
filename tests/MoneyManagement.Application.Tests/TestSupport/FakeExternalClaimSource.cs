using MoneyManagement.Application.Abstractions.NetWorth;
using NSubstitute;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IExternalClaimSource"/>. Use <see cref="Empty"/>
/// for the "accounts only" baseline (the shape every net-worth test had before
/// loans existed) and <see cref="With"/> to hand-build claims that no
/// <c>Loan.Create</c> call could produce — e.g. a future-dated claim.
/// </summary>
internal static class FakeExternalClaimSource
{
    public static IExternalClaimSource Empty() => With();

    public static IExternalClaimSource With(params ExternalClaim[] claims)
    {
        IExternalClaimSource source = Substitute.For<IExternalClaimSource>();
        source.GetHistoryAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExternalClaim>>(claims));
        return source;
    }
}
