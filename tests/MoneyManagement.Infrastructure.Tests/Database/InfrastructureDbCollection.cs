namespace MoneyManagement.Infrastructure.Tests.Database;

/// <summary>
/// Serializes every DB-backed test onto a single xUnit collection so they don't
/// run in parallel against the shared <c>money_management_inttest</c> schema.
/// Each test still isolates its own rows (unique GUIDs / currency codes) and
/// cleans up, so cross-test residue is impossible; this just avoids connection
/// churn and any incidental write contention.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InfrastructureDbCollection
{
    public const string Name = "InfrastructureDb";
}
