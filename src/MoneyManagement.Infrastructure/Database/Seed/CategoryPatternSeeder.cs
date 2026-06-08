using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Database.Seed;

/// <summary>
/// Backfills the built-in keyword -> category rules into
/// <c>category_patterns</c> so the import suggester reads from the database
/// instead of a hardcoded list. Idempotent: only inserts a pattern when no row
/// with the same (upper-cased) keyword already exists. Must run AFTER
/// <see cref="CategorySeeder"/> so the target categories exist.
/// </summary>
internal sealed class CategoryPatternSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<CategoryPatternSeeder> logger) : IHostedService
{
    private static readonly (string CategoryName, string[] Keywords)[] Defaults =
    [
        ("Groceries", ["LINELLA", "FAMILU IZMAIL", "V.COPOT-COM", "FELICIA"]),
        ("Restaurants", ["CASA DELLA PIZZA", "SUSHI MASTER", "MCDONALD", "MAX KEBAB", "TREI TAURI", "PREMIUM TEST"]),
        ("Transport", ["REGIA TRANSPORT", "LUKOIL"]),
        ("Subscriptions", ["APPLE.COM", "CLAUDE.AI", "SUBSCRIPTION"]),
        ("Shopping", ["TEMU", "CIP-69", "EPK COMPANY"]),
        ("Bills", ["ASP IALOVENI", "ENERGOCOM", "PREMIER ENERGY", "APA CANAL", "GOSPODARIA", "ORANGE", "MOLDOVA GAZ"]),
        ("Credit Payment", ["IUTE CREDIT", "OCN IUTE"]),
        ("Bank Fees", ["COMISION"]),
        ("Salary", ["SALARIU", "DIVIDENDE"]),
        ("Cashback", ["CASHBACK"]),
        ("Transfers", ["A2A DE INTRARE", "A2A DE IESIRE"]),
        ("Withdrawal", ["ATM"]),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Dictionary<string, Guid> categoriesByName = await db.Categories
            .Where(c => !c.IsArchived)
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        HashSet<string> existingKeywords = await db.CategoryPatterns
            .Select(p => p.Keyword)
            .ToHashSetAsync(cancellationToken);

        int inserted = 0;
        foreach ((string categoryName, string[] keywords) in Defaults)
        {
            if (!categoriesByName.TryGetValue(categoryName, out Guid categoryId))
            {
                logger.LogWarning(
                    "Cannot seed patterns for missing category {Name}.", categoryName);
                continue;
            }

            foreach (string keyword in keywords)
            {
                // Create normalizes to upper-case; compare against the same form.
                string normalized = keyword.Trim().ToUpperInvariant();
                if (existingKeywords.Contains(normalized))
                {
                    continue;
                }

                Result<CategoryPattern> result =
                    CategoryPattern.Create(keyword, categoryId, CategoryPatternSource.Seeded);
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "Failed to seed pattern {Keyword}: {Error}", keyword, result.Error);
                    continue;
                }

                db.CategoryPatterns.Add(result.Value);
                existingKeywords.Add(normalized);
                inserted++;
            }
        }

        if (inserted == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} default category patterns.", inserted);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
