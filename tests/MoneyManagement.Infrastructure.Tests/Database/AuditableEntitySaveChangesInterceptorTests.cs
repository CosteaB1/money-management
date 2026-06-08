using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// The interceptor stamps <c>CreatedAt</c>/<c>UpdatedAt</c> (UTC) on add and
/// refreshes <c>UpdatedAt</c> on modify. It's wired into the EF save pipeline,
/// so it's exercised through a real context against the throwaway
/// <c>money_management_inttest</c> DB (added rows are deleted on teardown).
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class AuditableEntitySaveChangesInterceptorTests : IAsyncLifetime
{
    private sealed class FixedClock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private readonly FixedClock _clock = new(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    private readonly ApplicationDbContext _context;
    private readonly List<Guid> _seeded = [];

    public AuditableEntitySaveChangesInterceptorTests()
    {
        _context = IntegrationDbContextFactory.Create(_clock);
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

    private Account NewAccount()
    {
        Account account = Account.Create(
            $"InterceptorTest {Guid.NewGuid():N}",
            AccountType.Cash,
            new Money(100m, ReportingCurrencies.Mdl),
            new DateOnly(2026, 1, 1),
            notes: null).Value;
        _seeded.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task SaveChanges_OnAdd_SetsCreatedAtAndUpdatedAtToUtcNow()
    {
        Account account = NewAccount();

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        account.CreatedAt.Should().Be(_clock.UtcNow);
        account.UpdatedAt.Should().Be(_clock.UtcNow);
        account.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task SaveChanges_OnModify_AdvancesUpdatedAt_LeavesCreatedAt()
    {
        DateTime createdAt = _clock.UtcNow;
        Account account = NewAccount();
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        // Advance the clock and mutate the entity.
        DateTime updatedAt = createdAt.AddHours(6);
        _clock.UtcNow = updatedAt;
        account.Archive();
        await _context.SaveChangesAsync();

        account.CreatedAt.Should().Be(createdAt);
        account.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void SaveChanges_SynchronousPath_StampsTimestamps()
    {
        // Drives the synchronous SavingChanges override (the async tests only hit
        // SavingChangesAsync).
        Account account = NewAccount();

        _context.Accounts.Add(account);
        _context.SaveChanges();

        account.CreatedAt.Should().Be(_clock.UtcNow);
        account.UpdatedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void SavingChanges_WithNullContext_IsNoOp()
    {
        // Defensive null-context guard in UpdateAuditFields. EF never hands us a
        // null context for a save interceptor, so it's reached here by invoking
        // the interceptor directly with event data carrying a null DbContext.
        var interceptor = new AuditableEntitySaveChangesInterceptor(_clock);
        var eventData = new Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData(
            eventDefinition: null!,
            messageGenerator: static (_, _) => string.Empty,
            context: null);

        Action sync = () => interceptor.SavingChanges(eventData, default);

        sync.Should().NotThrow();
    }

    [Fact]
    public async Task SaveChanges_PersistsStampedTimestamps()
    {
        Account account = NewAccount();
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        // Read back from the DB on a fresh, no-tracking query to prove the
        // interceptor-stamped values were actually persisted.
        Account reloaded = await _context.Accounts
            .AsNoTracking()
            .SingleAsync(a => a.Id == account.Id);

        reloaded.CreatedAt.Should().Be(_clock.UtcNow);
        reloaded.UpdatedAt.Should().Be(_clock.UtcNow);
    }
}
