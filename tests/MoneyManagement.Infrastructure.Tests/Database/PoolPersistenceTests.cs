using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyManagement.Application;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Pins the pool guarantees that live in the DATABASE rather than in the
/// domain, against the throwaway <c>money_management_inttest</c> schema:
/// <list type="bullet">
/// <item>
/// <c>units</c> / <c>nav_per_unit</c> really are <c>numeric(28,12)</c>. A NAV of
/// <c>1.000001234567</c> has to come back digit for digit — at the repo's usual
/// <c>numeric(18,2)</c> it would land as <c>1.00</c>, quantizing roughly 0.1%
/// per event per investor and breaking the pool's invariance identity. This is
/// irreversible once live data exists, so it is worth a real round-trip rather
/// than a model-metadata assertion.
/// </item>
/// <item>
/// The partial unique index really does allow only one owner per pool.
/// </item>
/// <item>
/// <c>PoolAccountOwnershipSource</c>'s <c>IgnoreQueryFilters()</c> really does
/// defeat <c>Pool</c>'s <c>is_archived</c> filter. That call CANNOT be pinned
/// against the Application suite's fake context: its provider is not an
/// <c>EntityQueryProvider</c>, so <c>IgnoreQueryFilters()</c> silently returns
/// the source unchanged and the assertion would pass with the call deleted.
/// </item>
/// </list>
/// Everything runs inside a transaction that is always rolled back, so the
/// shared DB is left exactly as found.
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class PoolPersistenceTests : IAsyncLifetime
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);

    private readonly ApplicationDbContext _context =
        IntegrationDbContextFactory.Create(new FixedClock(FixedNow));

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _context.DisposeAsync().AsTask();

    [Fact]
    public async Task PoolUnitEvent_RoundTrips_TwelveDecimalPlacesOfUnitsAndNav()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            (Pool pool, PoolParticipant owner) = await SeedPoolAsync();

            const decimal nav = 1.000001234567m;
            const decimal units = 123.456789012345m;

            // A cost share carries no cash, so nothing here is constrained by
            // the 2dp money scale - this isolates the unit columns.
            PoolUnitEvent costShare = PoolUnitEvent.Create(
                pool.Id,
                owner.Id,
                PoolUnitEventKind.CostShare,
                Today,
                units,
                nav,
                poolValuePreMoney: 1_050m,
                cash: null,
                settledOn: null,
                notes: null,
                poolCurrency: pool.Currency,
                poolInceptionDate: pool.InceptionDate,
                participantIsOwner: owner.IsOwner,
                clock: new FixedClock(FixedNow)).Value;

            _context.PoolUnitEvents.Add(costShare);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();

            PoolUnitEvent reloaded = await _context.PoolUnitEvents.SingleAsync(e => e.Id == costShare.Id);

            reloaded.NavPerUnit.Should().Be(nav);
            reloaded.Units.Should().Be(units);
            reloaded.UnitsDelta.Should().Be(-units);

            // Belt and braces: a numeric(18,2) column would return "1.00".
            reloaded.NavPerUnit.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be("1.000001234567");
            reloaded.Units.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be("123.456789012345");

            reloaded.Cash.Should().BeNull();
            reloaded.SettledOn.Should().BeNull();
            reloaded.MovementTransactionId.Should().BeNull();
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task PoolUnitEvent_RoundTrips_UnpaidDistributionCashAndNullSettledOn()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            (Pool pool, PoolParticipant owner) = await SeedPoolAsync();

            PoolUnitEvent distribution = PoolUnitEvent.Create(
                pool.Id,
                owner.Id,
                PoolUnitEventKind.Distribution,
                Today,
                units: 100m,
                navPerUnit: 1.25m,
                poolValuePreMoney: 2_050m,
                cash: new Money(125m, pool.Currency),
                settledOn: null,
                notes: "April close, paid in May",
                poolCurrency: pool.Currency,
                poolInceptionDate: pool.InceptionDate,
                participantIsOwner: owner.IsOwner,
                clock: new FixedClock(FixedNow)).Value;

            _context.PoolUnitEvents.Add(distribution);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();

            PoolUnitEvent reloaded = await _context.PoolUnitEvents.SingleAsync(e => e.Id == distribution.Id);

            // The paired cash_value / cash_currency columns recombine into Money?.
            reloaded.Cash.Should().Be(new Money(125m, pool.Currency));

            // Unpaid: the close happened, the transfer has not.
            reloaded.SettledOn.Should().BeNull();
            reloaded.MovementTransactionId.Should().BeNull();
            reloaded.PoolValuePreMoney.Should().Be(2_050m);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task PoolParticipants_SecondOwnerForTheSamePool_IsRejectedByTheFilteredUniqueIndex()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            (Pool pool, PoolParticipant _) = await SeedPoolAsync();

            PoolParticipant secondOwner = PoolParticipant.Create(
                pool.Id,
                "Impostor",
                isOwner: true,
                joinedOn: pool.InceptionDate,
                poolInceptionDate: pool.InceptionDate,
                clock: new FixedClock(FixedNow)).Value;

            _context.PoolParticipants.Add(secondOwner);

            Func<Task> act = () => _context.SaveChangesAsync();

            DbUpdateException failure = (await act.Should().ThrowAsync<DbUpdateException>()).Which;

            failure.InnerException.Should().BeOfType<Npgsql.PostgresException>()
                .Which.SqlState.Should().Be("23505", "the partial unique index must reject a second owner");
        }
        finally
        {
            _context.ChangeTracker.Clear();
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task PoolParticipants_SecondNonOwnerForTheSamePool_IsAllowed()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            (Pool pool, PoolParticipant _) = await SeedPoolAsync();

            // The unique index is filtered on is_owner, so friends are unbounded.
            foreach (string name in new[] { "Ion", "Vasile" })
            {
                _context.PoolParticipants.Add(PoolParticipant.Create(
                    pool.Id,
                    name,
                    isOwner: false,
                    joinedOn: pool.InceptionDate,
                    poolInceptionDate: pool.InceptionDate,
                    clock: new FixedClock(FixedNow)).Value);
            }

            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();

            List<PoolParticipant> participants = await _context.PoolParticipants
                .Where(p => p.PoolId == pool.Id)
                .ToListAsync();

            participants.Should().HaveCount(3);
            participants.Count(p => p.IsOwner).Should().Be(1);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task PoolAccountOwnershipSource_StillProjectsAnArchivedPoolThroughTheQueryFilter()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            var clock = new FixedClock(FixedNow);
            (Pool pool, PoolParticipant owner) = await SeedPoolAsync();

            PoolParticipant friend = PoolParticipant.Create(
                pool.Id,
                "Andrei",
                isOwner: false,
                joinedOn: pool.InceptionDate,
                poolInceptionDate: pool.InceptionDate,
                clock: clock).Value;

            _context.PoolParticipants.Add(friend);

            DateOnly arrival = pool.InceptionDate.AddDays(1);
            DateOnly exit = pool.InceptionDate.AddDays(2);

            _context.PoolUnitEvents.AddRange(
                UnitEvent(pool, owner.Id, PoolUnitEventKind.Seed, pool.InceptionDate, 0m, cash: null, participantIsOwner: true),
                UnitEvent(pool, friend.Id, PoolUnitEventKind.Subscription, arrival, 1_000m, new Money(1_000m, pool.Currency)),
                UnitEvent(pool, friend.Id, PoolUnitEventKind.Redemption, exit, 2_000m, new Money(1_000m, pool.Currency)));

            // The instance SeedPoolAsync handed back is detached (it clears the
            // change tracker), so archive the tracked row.
            Pool tracked = await _context.Pools.IgnoreQueryFilters().SingleAsync(p => p.Id == pool.Id);
            tracked.Archive(outsideUnitsOutstanding: 0m).IsSuccess.Should().BeTrue();

            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();

            // The query filter is REAL here, which is the whole reason this test
            // cannot live in the Application suite: FakeApplicationDbContext's
            // provider is not an EntityQueryProvider, so IgnoreQueryFilters() is a
            // no-op there and would pass with the call deleted.
            (await _context.Pools.AnyAsync(p => p.Id == pool.Id))
                .Should().BeFalse("Pool carries HasQueryFilter(p => !p.IsArchived)");
            (await _context.Pools.IgnoreQueryFilters().AnyAsync(p => p.Id == pool.Id))
                .Should().BeTrue();

            IReadOnlyList<AccountOwnership> history =
                await ResolveOwnershipSource().GetHistoryAsync(CancellationToken.None);

            AccountOwnership ownership =
                history.Should().ContainSingle(o => o.AccountId == pool.AccountId).Which;

            // Archiving needs zero outside units, so the LAST point is 1.0 either
            // way. What the filter would erase is the MIDDLE — the day the account
            // really was half somebody else's — and the net-worth trend reads
            // exactly those past dates.
            ownership.Points.Should().Equal(
                new OwnedFractionPoint(pool.InceptionDate, 1m),
                new OwnedFractionPoint(arrival, 0.5m),
                new OwnedFractionPoint(exit, 1m));
        }
        finally
        {
            _context.ChangeTracker.Clear();
            await tx.RollbackAsync();
        }
    }

    /// <summary>
    /// The registered <see cref="IAccountOwnershipSource"/>, resolved over THIS
    /// context so it sees the uncommitted rows. Goes through the container rather
    /// than <c>new</c> because the producer is internal to the Application
    /// assembly — and resolving it here doubles as a check that the Scrutor clause
    /// really does pick it up.
    /// </summary>
    private IAccountOwnershipSource ResolveOwnershipSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationDbContext>(_context);
        services.AddApplication();

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetServices<IAccountOwnershipSource>().Single();
    }

    /// <summary>A unit event at a NAV of exactly 1, so units and cash coincide.</summary>
    private static PoolUnitEvent UnitEvent(
        Pool pool,
        Guid participantId,
        PoolUnitEventKind kind,
        DateOnly occurredOn,
        decimal poolValuePreMoney,
        Money? cash,
        bool participantIsOwner = false) =>
        PoolUnitEvent.Create(
            pool.Id,
            participantId,
            kind,
            occurredOn,
            units: 1_000m,
            navPerUnit: 1m,
            poolValuePreMoney,
            cash,
            settledOn: cash is null ? null : occurredOn,
            notes: null,
            poolCurrency: pool.Currency,
            poolInceptionDate: pool.InceptionDate,
            participantIsOwner,
            clock: new FixedClock(FixedNow)).Value;

    /// <summary>
    /// Creates a fresh account (the pool's account_id is UNIQUE, so an existing
    /// account might already be taken), a pool on it, and its owner participant.
    /// </summary>
    private async Task<(Pool Pool, PoolParticipant Owner)> SeedPoolAsync()
    {
        var clock = new FixedClock(FixedNow);
        DateOnly inception = new(2026, 4, 1);

        Account account = Account.Create(
            $"Pool inttest {Guid.CreateVersion7()}",
            AccountType.CryptoExchange,
            Money.Zero("USD"),
            inception,
            notes: null).Value;
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        Pool pool = Pool.Create(
            account.Id,
            "Inttest pool",
            "USD",
            accountCurrency: "USD",
            inceptionDate: inception,
            notes: null,
            clock: clock).Value;
        _context.Pools.Add(pool);
        await _context.SaveChangesAsync();

        PoolParticipant owner = PoolParticipant.Create(
            pool.Id,
            "Me",
            isOwner: true,
            joinedOn: inception,
            poolInceptionDate: inception,
            clock: clock).Value;
        _context.PoolParticipants.Add(owner);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        return (pool, owner);
    }
}
