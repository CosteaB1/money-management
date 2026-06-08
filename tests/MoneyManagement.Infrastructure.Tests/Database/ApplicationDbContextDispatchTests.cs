using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Pins <see cref="ApplicationDbContext.SaveChangesAsync"/>'s save-then-dispatch
/// behaviour: domain events raised by tracked entities are collected, the tracker
/// is cleared of them, and after the underlying save the events are handed to the
/// <c>IDomainEventsDispatcher</c>. Runs against the throwaway
/// <c>money_management_inttest</c> DB and deletes the row it adds on teardown.
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class ApplicationDbContextDispatchTests : IAsyncLifetime
{
    private sealed class CapturingDispatcher : IDomainEventsDispatcher
    {
        public List<IDomainEvent> Dispatched { get; } = [];

        public Task DispatchAsync(
            IReadOnlyList<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default)
        {
            Dispatched.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    private readonly CapturingDispatcher _dispatcher = new();
    private readonly ApplicationDbContext _context;
    private readonly List<Guid> _seeded = [];

    public ApplicationDbContextDispatchTests()
    {
        // Reuse the guarded connection string so this can never reach the real/QA DB.
        DbContextOptions<ApplicationDbContext> options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(IntegrationDbContextFactory.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options;

        _context = new ApplicationDbContext(options, _dispatcher);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_seeded.Count > 0)
        {
            await _context.Accounts
                .IgnoreQueryFilters()
                .Where(a => _seeded.Contains(a.Id))
                .ExecuteDeleteAsync();
        }

        await _context.DisposeAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_DispatchesDomainEventsRaisedByTrackedEntities()
    {
        Account account = Account.Create(
            $"DispatchTest {Guid.NewGuid():N}",
            AccountType.Cash,
            new Money(100m, ReportingCurrencies.Mdl),
            new DateOnly(2026, 1, 1),
            notes: null).Value;
        _seeded.Add(account.Id);
        account.GetDomainEvents().Should().ContainSingle(
            "the create factory raises AccountCreatedDomainEvent");

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        _dispatcher.Dispatched.Should().ContainSingle()
            .Which.Should().BeOfType<AccountCreatedDomainEvent>()
            .Which.AccountId.Should().Be(account.Id);

        // The tracker's events were cleared as part of the save.
        account.GetDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_WithNoDomainEvents_DoesNotDispatch()
    {
        // Insert an account, then clear its events before save so the
        // domainEvents.Count == 0 short-circuit (no dispatch) is taken.
        Account account = Account.Create(
            $"DispatchTest {Guid.NewGuid():N}",
            AccountType.Cash,
            new Money(50m, ReportingCurrencies.Mdl),
            new DateOnly(2026, 1, 1),
            notes: null).Value;
        _seeded.Add(account.Id);
        account.ClearDomainEvents();

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        _dispatcher.Dispatched.Should().BeEmpty();
    }
}
