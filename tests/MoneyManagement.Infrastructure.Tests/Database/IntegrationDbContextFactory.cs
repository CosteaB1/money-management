using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Builds an <see cref="ApplicationDbContext"/> wired to the dedicated throwaway
/// Postgres database <c>money_management_inttest</c> — NEVER the real
/// <c>money_management</c> DB or the manual-QA <c>money_management_test</c> DB
/// (repo rule, see CLAUDE.md). Tests that touch the DB seed rows with unique
/// GUIDs / unique currency codes and clean up after themselves, so they can run
/// against the persistent inttest schema without colliding with each other or
/// leaving residue.
/// </summary>
internal static class IntegrationDbContextFactory
{
    // Same local Postgres / throwaway database as the Api integration suite's
    // CustomWebApplicationFactory. The password is read from the POSTGRES_PASSWORD
    // environment variable (see .env.example) so no secret lives in source; the
    // keyword form is used because passwords may contain URL-unsafe chars like '&%'.
    public static readonly string ConnectionString =
        $"Host=localhost;Database=money_management_inttest;Username=postgres;Password=" +
        (Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres");

    public static ApplicationDbContext Create(IDateTimeProvider? clock = null)
    {
        // Hard guard: refuse to build a context against anything other than the
        // dedicated throwaway DB, mirroring the Api suite's safety net so a
        // mistyped connection string can never reach the real/QA database.
        string database = ExtractDatabase(ConnectionString);
        if (!string.Equals(database, "money_management_inttest", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Infrastructure-test DB guard tripped: resolved database '{database}', " +
                "which is not the dedicated 'money_management_inttest'.");
        }

        DbContextOptionsBuilder<ApplicationDbContext> builder =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention();

        if (clock is not null)
        {
            builder.AddInterceptors(new AuditableEntitySaveChangesInterceptor(clock));
        }

        // Domain-event dispatch is irrelevant to these tests; a no-op keeps
        // SaveChangesAsync from needing a service provider.
        return new ApplicationDbContext(builder.Options, new NoOpDomainEventsDispatcher());
    }

    private static string ExtractDatabase(string connectionString)
    {
        foreach (string part in connectionString.Split(
            ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] keyValue = part.Split('=', 2);
            if (keyValue.Length == 2 && keyValue[0].Trim().Equals("Database", StringComparison.OrdinalIgnoreCase))
            {
                return keyValue[1].Trim();
            }
        }

        return string.Empty;
    }

    private sealed class NoOpDomainEventsDispatcher : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
