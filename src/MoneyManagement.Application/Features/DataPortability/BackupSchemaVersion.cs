namespace MoneyManagement.Application.Features.DataPortability;

/// <summary>
/// Versions the <see cref="BackupDocument"/> wire format. Bump
/// <see cref="Current"/> whenever a column is added, removed, or its meaning
/// changes — the import handler refuses any document whose
/// <see cref="BackupDocument.SchemaVersion"/> doesn't equal <see cref="Current"/>.
/// </summary>
public static class BackupSchemaVersion
{
    public const int Current = 4;
}
