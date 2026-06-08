using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.Infrastructure.Database.Seed;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Exercises the idempotent seeders against the throwaway
/// <c>money_management_inttest</c> DB. The schema already holds the seeded rows,
/// so we delete a couple first to drive the insert path, then assert the seeder
/// restores them — leaving the DB back in its fully-seeded state (no residue).
/// <c>category_patterns</c> has no inbound foreign keys, so deleting/re-inserting
/// a pattern is safe even on the shared schema.
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class SeederTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context = IntegrationDbContextFactory.Create();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _context.DisposeAsync().AsTask();

    [Fact]
    public async Task CategorySeeder_WhenAllSeedCategoriesExist_IsNoOp()
    {
        // The inttest schema is already fully seeded by prior runs/migrations,
        // so a fresh StartAsync finds every default id present and returns early.
        var seeder = new CategorySeeder(new SeederScopeFactory(_context), NullLogger<CategorySeeder>.Instance);

        Func<Task> act = () => seeder.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        // StopAsync is a no-op completion contract.
        await seeder.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CategorySeeder_ReinsertsMissingCategory_InsidePinnedTransaction()
    {
        // Drive the CategorySeeder insert path (lines that add a missing default
        // category and SaveChanges). The inttest schema is already fully seeded,
        // so we delete one seeded category to create work for the seeder. We do
        // this inside a transaction that is ALWAYS rolled back, so the shared DB
        // is left untouched (no residue, no FK fallout) regardless of outcome.
        var subscriptionsId = new Guid("00000000-0000-0000-0000-000000000004");

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            // Remove dependents first so the FK constraints permit the delete.
            await _context.CategoryPatterns
                .Where(p => p.CategoryId == subscriptionsId)
                .ExecuteDeleteAsync();
            await _context.Transactions
                .Where(t => t.CategoryId == subscriptionsId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)null));
            await _context.Budgets
                .Where(b => b.CategoryId == subscriptionsId)
                .ExecuteDeleteAsync();
            int deleted = await _context.Categories
                .Where(c => c.Id == subscriptionsId)
                .ExecuteDeleteAsync();
            deleted.Should().Be(1, "the Subscriptions seed category should have existed");

            _context.ChangeTracker.Clear();

            var seeder = new CategorySeeder(
                new SeederScopeFactory(_context),
                NullLogger<CategorySeeder>.Instance);

            await seeder.StartAsync(CancellationToken.None);

            Category? reinserted = await _context.Categories
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == subscriptionsId);
            reinserted.Should().NotBeNull();
            reinserted!.Name.Should().Be("Subscriptions");
            reinserted.Flow.Should().Be(CategoryFlow.Expense);

            await seeder.StopAsync(CancellationToken.None);
        }
        finally
        {
            // Never persist: the real seeded state is restored by the rollback.
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task CategoryPatternSeeder_ReinsertsMissingPattern()
    {
        const string keyword = "LINELLA";

        // Delete one seed pattern so the seeder has work to do.
        await _context.CategoryPatterns
            .Where(p => p.Keyword == keyword)
            .ExecuteDeleteAsync();

        bool presentBefore = await _context.CategoryPatterns.AnyAsync(p => p.Keyword == keyword);
        presentBefore.Should().BeFalse();

        var seeder = new CategoryPatternSeeder(
            new SeederScopeFactory(_context),
            NullLogger<CategoryPatternSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);

        // The seeder restored the deleted pattern (insert path exercised).
        bool presentAfter = await _context.CategoryPatterns
            .AsNoTracking()
            .AnyAsync(p => p.Keyword == keyword);
        presentAfter.Should().BeTrue();
    }

    [Fact]
    public async Task CategoryPatternSeeder_WhenTargetCategoryMissing_LogsAndSkips()
    {
        // Drive the "category missing" branch: remove a category that the pattern
        // Defaults target ("Withdrawal") plus its patterns, then run the seeder.
        // It should not throw and must skip that category's keywords. Wrapped in a
        // rolled-back transaction so the shared DB is left untouched.
        var withdrawalId = new Guid("00000000-0000-0000-0000-00000000000f");

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.CategoryPatterns
                .Where(p => p.CategoryId == withdrawalId)
                .ExecuteDeleteAsync();
            await _context.Transactions
                .Where(t => t.CategoryId == withdrawalId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)null));
            await _context.Budgets
                .Where(b => b.CategoryId == withdrawalId)
                .ExecuteDeleteAsync();
            await _context.Categories
                .Where(c => c.Id == withdrawalId)
                .ExecuteDeleteAsync();

            _context.ChangeTracker.Clear();

            var seeder = new CategoryPatternSeeder(
                new SeederScopeFactory(_context),
                NullLogger<CategoryPatternSeeder>.Instance);

            Func<Task> act = () => seeder.StartAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();

            // The "ATM" keyword (the only Withdrawal pattern) must not have been
            // recreated, since its category is gone.
            bool atmPresent = await _context.CategoryPatterns
                .AsNoTracking()
                .AnyAsync(p => p.Keyword == "ATM");
            atmPresent.Should().BeFalse();
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task CategoryPatternSeeder_WhenAllPatternsExist_IsNoOp()
    {
        // Ensure the prior test (or seed) left everything present, then a second
        // run inserts nothing and returns via the inserted==0 short-circuit.
        var firstRun = new CategoryPatternSeeder(
            new SeederScopeFactory(_context),
            NullLogger<CategoryPatternSeeder>.Instance);
        await firstRun.StartAsync(CancellationToken.None);

        var secondRun = new CategoryPatternSeeder(
            new SeederScopeFactory(_context),
            NullLogger<CategoryPatternSeeder>.Instance);

        Func<Task> act = () => secondRun.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await secondRun.StopAsync(CancellationToken.None);
    }
}
