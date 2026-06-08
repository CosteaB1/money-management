using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoneyManagement.Application.Abstractions.Backup;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Imports;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Infrastructure.Backup;
using MoneyManagement.Infrastructure.Database;
using MoneyManagement.Infrastructure.Database.Seed;
using MoneyManagement.Infrastructure.Events;
using MoneyManagement.Infrastructure.FxRates;
using MoneyManagement.Infrastructure.Imports;
using MoneyManagement.Infrastructure.Time;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // The qa launch profile sets UseTestDatabase=true to point at the isolated
        // test database without carrying a connection string (and its password) in
        // tracked config — both connection strings live in user-secrets / env.
        string connectionStringKey = configuration.GetValue<bool>("UseTestDatabase") ? "Test" : "Default";
        string connectionString = configuration.GetConnectionString(connectionStringKey)
            ?? throw new InvalidOperationException($"ConnectionStrings:{connectionStringKey} is not configured.");

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<IDomainEventsDispatcher, DomainEventsDispatcher>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddSingleton<IBankStatementParser, MaibStatementParser>();
        services.AddScoped<ICategorySuggester, CategorySuggester>();
        services.AddSingleton<ITransferDetector, SubstringTransferDetector>();
        services.AddScoped<IFxConverter, EfFxConverter>();
        services.AddScoped<IBackupStore, EfBackupStore>();

        // BNM auto-fetch — options come from "Fx:AutoFetch" in appsettings.
        services.Configure<FxAutoFetchOptions>(configuration.GetSection("Fx:AutoFetch"));
        services.AddHttpClient<IBnmRateProvider, BnmRateProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHostedService<CategorySeeder>();
        // Must run after CategorySeeder so the categories the patterns point at exist.
        services.AddHostedService<CategoryPatternSeeder>();
        services.AddHostedService<BnmAutoFetchService>();

        return services;
    }
}
