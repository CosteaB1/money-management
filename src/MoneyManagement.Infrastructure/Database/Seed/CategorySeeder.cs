using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Database.Seed;

internal sealed class CategorySeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<CategorySeeder> logger) : IHostedService
{
    private static readonly (Guid Id, string Name, CategoryFlow Flow, string Color)[] Defaults =
    [
        (new Guid("00000000-0000-0000-0000-000000000001"), "Groceries",          CategoryFlow.Expense, "#16a34a"),
        (new Guid("00000000-0000-0000-0000-000000000002"), "Restaurants",        CategoryFlow.Expense, "#f97316"),
        (new Guid("00000000-0000-0000-0000-000000000003"), "Transport",          CategoryFlow.Expense, "#0ea5e9"),
        (new Guid("00000000-0000-0000-0000-000000000004"), "Subscriptions",      CategoryFlow.Expense, "#a855f7"),
        (new Guid("00000000-0000-0000-0000-000000000005"), "Shopping",           CategoryFlow.Expense, "#ec4899"),
        (new Guid("00000000-0000-0000-0000-000000000006"), "Bills",              CategoryFlow.Expense, "#eab308"),
        (new Guid("00000000-0000-0000-0000-000000000011"), "Home",               CategoryFlow.Expense, "#6366f1"),
        (new Guid("00000000-0000-0000-0000-000000000007"), "Salary",             CategoryFlow.Income,  "#22c55e"),
        (new Guid("00000000-0000-0000-0000-000000000008"), "Transfers",          CategoryFlow.Both,    "#64748b"),
        (new Guid("00000000-0000-0000-0000-000000000009"), "Other expenses",     CategoryFlow.Expense, "#94a3b8"),
        (new Guid("00000000-0000-0000-0000-000000000010"), "Other income",       CategoryFlow.Income,  "#84cc16"),
        (new Guid("00000000-0000-0000-0000-00000000000c"), "Cashback",           CategoryFlow.Income,  "#14b8a6"),
        (new Guid("00000000-0000-0000-0000-00000000000d"), "Bank Fees",          CategoryFlow.Expense, "#92400e"),
        (new Guid("00000000-0000-0000-0000-00000000000b"), "Credit Payment",     CategoryFlow.Expense, "#dc2626"),
        (SeededCategories.BalanceAdjustmentId,             "Balance Adjustment", CategoryFlow.Both,    "#0891b2"),
        (SeededCategories.InvestmentId,                    "Investment",         CategoryFlow.Both,    "#2563eb"),
        (SeededCategories.WithdrawalId,                    "Withdrawal",         CategoryFlow.Both,    "#b45309"),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Backfill any missing seeded category by id so adding new entries to
        // Defaults (e.g. Phase 4's "Balance Adjustment") doesn't require dropping
        // the table. We never rename or recolor existing rows.
        HashSet<Guid> existingIds = await db.Categories
            .Select(c => c.Id)
            .ToHashSetAsync(cancellationToken);

        int inserted = 0;
        foreach ((Guid id, string name, CategoryFlow flow, string color) in Defaults)
        {
            if (existingIds.Contains(id))
            {
                continue;
            }

            Result<Category> result = Category.Create(name, flow, parentId: null, color: color, icon: null);
            if (result.IsFailure)
            {
                logger.LogWarning("Failed to seed category {Name}: {Error}", name, result.Error);
                continue;
            }

            Category category = result.Value;
            SetEntityId(category, id);
            db.Categories.Add(category);
            inserted++;
        }

        if (inserted == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} default categories.", inserted);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void SetEntityId(Category category, Guid id) =>
        typeof(SharedKernel.Entity)
            .GetProperty(nameof(SharedKernel.Entity.Id))!
            .SetValue(category, id);
}
