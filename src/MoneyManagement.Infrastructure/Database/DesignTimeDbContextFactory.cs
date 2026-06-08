using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Database;

/// <summary>
/// Used by `dotnet ef` at design time so migrations can be generated without
/// spinning up the full host. The connection string is a placeholder - it is
/// never actually opened during `migrations add`.
/// </summary>
// Excluded from coverage: design-time-only EF tooling entry point. It is invoked
// exclusively by `dotnet ef` while generating migrations and never runs at
// application runtime, so it is unreachable from any test.
[ExcludeFromCodeCoverage(Justification = "Design-time `dotnet ef` tooling entry point; never executed at runtime.")]
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=money_management;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options, new NoOpDomainEventsDispatcher());
    }

    private sealed class NoOpDomainEventsDispatcher : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
