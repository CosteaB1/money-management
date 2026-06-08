using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.Infrastructure.Tests.Database;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Exercises the private parameterless EF Core constructors that are only ever
/// invoked when the provider materialises an entity from a query result (never
/// from application code). We insert one <see cref="ImportBatch"/> and one
/// <see cref="BudgetPeriod"/>, then read them back with a tracking query so EF
/// runs the materialisation constructors. Everything happens inside a
/// transaction that is always rolled back, so the shared
/// <c>money_management_inttest</c> DB is left untouched.
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class EfMaterializationTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context = IntegrationDbContextFactory.Create();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _context.DisposeAsync().AsTask();

    [Fact]
    public async Task ImportBatch_RoundTrips_ThroughEfMaterializationConstructor()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            // Need a real account for the FK.
            Guid accountId = await _context.Accounts
                .Select(a => a.Id)
                .FirstAsync();

            ImportBatch batch = ImportBatch.Create(
                accountId,
                "materialization-test.pdf",
                "deadbeef",
                BankSource.Maib,
                DateTime.UtcNow,
                importedCount: 3,
                skippedDuplicateCount: 1).Value;

            _context.ImportBatches.Add(batch);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();

            ImportBatch reloaded = await _context.ImportBatches
                .SingleAsync(b => b.Id == batch.Id);

            reloaded.FileName.Should().Be("materialization-test.pdf");
            reloaded.BankSource.Should().Be(BankSource.Maib);
            reloaded.ImportedCount.Should().Be(3);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task BudgetPeriod_RoundTrips_ThroughEfMaterializationConstructor()
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _context.Database.BeginTransactionAsync();
        try
        {
            // A budget needs a category FK; reuse an existing seeded category.
            Guid categoryId = await _context.Categories
                .Select(c => c.Id)
                .FirstAsync();

            Budget budget = Budget.Create(categoryId, new Money(500m, ReportingCurrencies.Mdl)).Value;
            _context.Budgets.Add(budget);

            BudgetPeriod period = BudgetPeriod.Create(budget.Id, 2026, 6).Value;
            period.AddSpend(123.45m);
            _context.BudgetPeriods.Add(period);

            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();

            BudgetPeriod reloaded = await _context.BudgetPeriods
                .SingleAsync(p => p.Id == period.Id);

            reloaded.Year.Should().Be(2026);
            reloaded.Month.Should().Be(6);
            reloaded.Spent.Amount.Should().Be(123.45m);
            reloaded.Spent.Currency.Should().Be(ReportingCurrencies.Mdl);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
