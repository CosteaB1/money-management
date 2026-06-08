using Microsoft.Extensions.DependencyInjection;
using MoneyManagement.Infrastructure.Database;

namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Minimal <see cref="IServiceScopeFactory"/> that hands the seeders a single
/// shared <see cref="ApplicationDbContext"/> bound to the throwaway
/// <c>money_management_inttest</c> DB. The seeders only resolve
/// <see cref="ApplicationDbContext"/> from the scope, so nothing else is needed.
/// </summary>
internal sealed class SeederScopeFactory(ApplicationDbContext context) : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    public IServiceScope CreateScope() => this;

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(ApplicationDbContext) ? context : null;

    // The scope must NOT dispose the shared context — the test owns its lifetime.
    public void Dispose()
    {
    }
}
