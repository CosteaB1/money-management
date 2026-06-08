namespace MoneyManagement.Api.Tests;

/// <summary>
/// Shared collection so the API host (and its DB migration + seeding on first
/// boot) is created once and reused across every test class in the assembly.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    public const string Name = "Api integration tests";
}
