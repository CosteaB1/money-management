using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Database;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IDomainEventsDispatcher domainEventsDispatcher) : DbContext(options), IApplicationDbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryPattern> CategoryPatterns => Set<CategoryPattern>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetPeriod> BudgetPeriods => Set<BudgetPeriod>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsGoalContribution> SavingsGoalContributions => Set<SavingsGoalContribution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect events before SaveChanges so we don't lose them when the tracker resets state.
        // IMPORTANT: GetDomainEvents() returns a read-only VIEW over the entity's
        // backing list, so the events must be copied into a new list BEFORE
        // ClearDomainEvents() empties that backing list — otherwise the captured
        // sequence reads back as empty and nothing is ever dispatched.
        var domainEvents = ChangeTracker
            .Entries<Entity>()
            .SelectMany(e =>
            {
                List<IDomainEvent> events = [.. e.Entity.GetDomainEvents()];
                e.Entity.ClearDomainEvents();
                return events;
            })
            .ToList();

        int result = await base.SaveChangesAsync(cancellationToken);

        // v1: simple save-then-dispatch. No outbox; acceptable for single-user self-hosted.
        if (domainEvents.Count > 0)
        {
            await domainEventsDispatcher.DispatchAsync(domainEvents, cancellationToken);
        }

        return result;
    }
}
