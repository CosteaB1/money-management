using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Infrastructure.Database;

namespace MoneyManagement.Api.Extensions;

public static class MigrationExtensions
{
    // Excluded from coverage: runs once at host startup against the live database.
    // The integration suite points at an already-migrated inttest DB, so the
    // pending-migrations apply path (logging + Database.Migrate) is only reachable
    // against a fresh DB during real startup, never from a test.
    [ExcludeFromCodeCoverage(Justification = "Host-startup migration apply; only runs against a fresh live DB, not under test.")]
    public static void ApplyMigrations(this WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();
        ILogger<ApplicationDbContext> logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        using ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Surface the active database on every startup so a wrong launch profile
        // (e.g. the `qa` profile vs. the default real-data profile) is obvious in
        // the console before any data is written. See QA.md for the real/test split.
        logger.LogInformation(
            "Using database '{Database}' on host '{Host}'.",
            db.Database.GetDbConnection().Database,
            db.Database.GetDbConnection().DataSource);

        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));

        // Migrate() creates the database itself if the configured one is missing,
        // so a fresh dev box only needs Postgres running — no manual createdb step.
        db.Database.Migrate();

        logger.LogInformation("Migrations applied.");
    }
}
