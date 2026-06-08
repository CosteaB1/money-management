using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Boots the real API in-memory against a DEDICATED Postgres database
/// (<c>money_management_inttest</c>) so integration tests can write/wipe freely
/// without ever touching the real <c>money_management</c> DB or the manual-QA
/// <c>money_management_test</c> DB (repo rule, see CLAUDE.md / QA.md).
///
/// Overrides applied:
/// <list type="bullet">
///   <item><c>ConnectionStrings:Default</c> → <c>money_management_inttest</c>, via
///   <see cref="Microsoft.AspNetCore.Hosting.HostingAbstractionsWebHostBuilderExtensions.UseSetting"/>.
///   That writes the value into the web-host builder configuration, which the
///   <c>WebApplicationBuilder</c> layers ABOVE the app-configuration sources — so it
///   beats the real-DB connection string that <c>Program</c> loads from user-secrets
///   in Development (a plain <c>ConfigureAppConfiguration</c> source does NOT, which
///   previously let the app bind to the real DB). The <c>CreateHost</c> guard below
///   fails closed if this layering ever regresses.</item>
///   <item><c>Fx:AutoFetch:Enabled = false</c> → the <c>BnmAutoFetchService</c>
///   BackgroundService no-ops, so the test host never hits the network
///   (https://www.bnm.md) and there is no background log noise.</item>
/// </list>
///
/// The environment is forced to <c>Development</c> because <c>Program.cs</c> only
/// calls <c>ApplyMigrations()</c> (create + migrate the DB) when
/// <c>IsDevelopment()</c>. <c>Migrate()</c> creates the database on first boot, so
/// no manual createdb step is required. The category/pattern seeders
/// (<see cref="Microsoft.Extensions.Hosting.IHostedService"/>) are left enabled —
/// they seed the reference data several tests rely on.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Local Postgres on a throwaway database. The password is read from the
    // POSTGRES_PASSWORD environment variable (see .env.example) so no secret lives
    // in source; the keyword form is used because passwords may contain URL-unsafe
    // characters like '&%'.
    public static readonly string IntegrationTestConnectionString =
        $"Host=localhost;Database=money_management_inttest;Username=postgres;Password={IntegrationTestPassword}";

    private static string IntegrationTestPassword =>
        Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting targets the web-host builder configuration, which sits ABOVE
        // the app-configuration sources in the WebApplicationBuilder precedence chain
        // — required so the real-DB connection string Program loads from user-secrets
        // (Development) cannot win. The CreateHost guard verifies this held.
        builder.UseSetting("ConnectionStrings:Default", IntegrationTestConnectionString);
        builder.UseSetting("Fx:AutoFetch:Enabled", "false");
    }

    /// <summary>
    /// Hard safety net: refuse to run the integration suite against any database
    /// other than the dedicated throwaway <c>money_management_inttest</c>. If the
    /// config layering ever regresses (the bug that once let the suite bind to the
    /// real <c>money_management</c> DB and leak test rows), this throws at host
    /// startup — BEFORE any test body can create/wipe data — instead of silently
    /// mutating the wrong database. Fails closed: a real-DB URI, the QA DB, or an
    /// unparseable string all resolve to "not money_management_inttest" → throw.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();
        string resolved = configuration.GetConnectionString("Default") ?? string.Empty;
        string database = ExtractDatabase(resolved);

        if (!string.Equals(database, "money_management_inttest", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration-test DB guard tripped: the app resolved database '{database}', " +
                "which is not the dedicated 'money_management_inttest'. Refusing to run integration " +
                "tests against the real or QA database. Check CustomWebApplicationFactory's connection-string override.");
        }

        return host;
    }

    // Pull the Database= value out of a keyword-form Npgsql connection string.
    // A URI-form or otherwise unparseable string yields "" (which trips the guard).
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
}
