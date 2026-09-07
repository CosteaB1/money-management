using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Dashboard.GetNetWorth;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Abstractions.NetWorth;

/// <summary>
/// A tripwire on how the two net-worth collaborator seams are wired into the
/// container, not on what they compute.
/// </summary>
public sealed class NetWorthSourceRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // The collaborators the Application assembly does not provide itself.
        services.AddSingleton(FakeApplicationDbContext.Create());
        services.AddSingleton(Substitute.For<IFxConverter>());
        services.AddSingleton(Substitute.For<IDateTimeProvider>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddApplication();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void ExactlyOneExternalClaimSource_IsRegistered()
    {
        // GetNetWorthQueryHandler and GetNetWorthTrendQueryHandler take
        // IExternalClaimSource SINGULAR. With Scrutor scanning the assembly, the
        // day a second implementation appears the container hands the handlers
        // the LAST registration and the other source's claims vanish from net
        // worth — silently, with no failing test and no log line.
        //
        // If this ever goes red, the fix is to switch both handlers to
        // IEnumerable<IExternalClaimSource> (as the ownership seam already does),
        // NOT to bump the expected count.
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IExternalClaimSource>().Should().HaveCount(1);
    }

    [Fact]
    public void ExactlyOneAccountOwnershipSource_IsRegistered_AndItIsThePoolSource()
    {
        // The registration pin for the pools read slice. Until this commit the
        // seam shipped with NO producer and this test asserted emptiness; the
        // producer is PoolAccountOwnershipSource and it is picked up by the
        // Scrutor clause in DependencyInjection, not by a hand-written line.
        //
        // Unlike the claim seam above, a SECOND ownership source would not be
        // silently swallowed — both handlers take IEnumerable<> and
        // AccountOwnershipLedger drains every source. What it WOULD do is make
        // "the owner's share of this account" ambiguous the moment two producers
        // named the same account, which the ledger resolves first-wins and
        // therefore by registration order. So the count is pinned here too: a new
        // producer must arrive with a decision about composition, not by accident.
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        IAccountOwnershipSource source =
            scope.ServiceProvider.GetServices<IAccountOwnershipSource>().Should().ContainSingle().Which;

        source.Should().BeOfType<PoolAccountOwnershipSource>();
    }

    [Fact]
    public void NetWorthCardHandler_ResolvesWithItsOwnershipSourceInjected()
    {
        // The handlers take IEnumerable<IAccountOwnershipSource> precisely so
        // that zero registrations stays a valid, behaviour-neutral state for an
        // app with no pools. That is a compile-time shape; this checks the whole
        // graph still resolves now that a producer actually exists — the ownership
        // source is SCOPED because it queries the request's DbContext, and a
        // captive-dependency mistake would only show up through the container.
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        IQueryHandler<GetNetWorthQuery, NetWorthDto> handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<GetNetWorthQuery, NetWorthDto>>();

        handler.Should().NotBeNull();
    }
}
