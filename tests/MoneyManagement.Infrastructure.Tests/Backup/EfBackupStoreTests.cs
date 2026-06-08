using FluentAssertions;
using MoneyManagement.Application.Features.DataPortability;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Infrastructure.Backup;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.Infrastructure.Tests.Database;

namespace MoneyManagement.Infrastructure.Tests.Backup;

/// <summary>
/// Drives <see cref="EfBackupStore"/>'s restore inserts against the throwaway
/// <c>money_management_inttest</c> DB. The round-trip here covers the two branches
/// the API-level <c>BackupRoundTripTests</c> can't reach with the default seed
/// data: inserting <c>budget_periods</c> and ordering a parent-before-child
/// category pair (the topological-sort recursion).
///
/// Strategy: capture the current export as a baseline, restore a synthetic
/// document built on top of it (extra parent/child categories + a budget and its
/// period), assert via a fresh export that the new rows survived, then restore the
/// baseline so the shared DB is left exactly as found (no residue).
/// </summary>
[Collection(InfrastructureDbCollection.Name)]
public sealed class EfBackupStoreTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _context = IntegrationDbContextFactory.Create();
    private BackupDocument _baseline = null!;

    public async Task InitializeAsync()
    {
        var store = new EfBackupStore(_context);
        _baseline = await store.ExportAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        // Restore the captured baseline so the DB is returned to its found state,
        // then drop the context.
        var store = new EfBackupStore(_context);
        await store.RestoreAsync(_baseline, CancellationToken.None);
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task RestoreAsync_InsertsBudgetPeriods_AndOrdersParentCategoryBeforeChild()
    {
        DateTime now = DateTime.UtcNow;
        var parentId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var budgetId = Guid.CreateVersion7();
        var periodId = Guid.CreateVersion7();

        // Child is listed BEFORE its parent in the document so the restore's
        // topological sort must reorder it (exercising the Visit(parent) branch).
        var categories = new List<CategoryBackup>(_baseline.Categories)
        {
            new(childId, "IntTest Child", parentId, "#123456", null, CategoryFlow.Expense, false, now, now),
            new(parentId, "IntTest Parent", null, "#654321", null, CategoryFlow.Expense, false, now, now),
        };

        var budgets = new List<BudgetBackup>(_baseline.Budgets)
        {
            new(budgetId, childId, 500m, ReportingCurrencies.Mdl, false, now, now),
        };

        var budgetPeriods = new List<BudgetPeriodBackup>(_baseline.BudgetPeriods)
        {
            new(periodId, budgetId, 2026, 5, 123.45m, ReportingCurrencies.Mdl, now, now),
        };

        BackupDocument document = _baseline with
        {
            Categories = categories,
            Budgets = budgets,
            BudgetPeriods = budgetPeriods,
        };

        var store = new EfBackupStore(_context);
        ImportDataResult result = await store.RestoreAsync(document, CancellationToken.None);

        result.BudgetPeriods.Should().Be(budgetPeriods.Count);
        result.Budgets.Should().Be(budgets.Count);
        result.Categories.Should().Be(categories.Count);

        // Re-export to prove the parent/child ordering held (the child's parent_id
        // FK would have failed had the child been inserted first) and the period
        // row landed.
        BackupDocument after = await store.ExportAsync(CancellationToken.None);

        CategoryBackup child = after.Categories.Single(c => c.Id == childId);
        child.ParentId.Should().Be(parentId);
        after.Categories.Should().ContainSingle(c => c.Id == parentId);

        BudgetPeriodBackup period = after.BudgetPeriods.Single(p => p.Id == periodId);
        period.BudgetId.Should().Be(budgetId);
        period.SpentAmount.Should().Be(123.45m);
        period.Year.Should().Be(2026);
        period.Month.Should().Be(5);
    }
}
