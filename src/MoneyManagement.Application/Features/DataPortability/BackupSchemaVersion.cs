namespace MoneyManagement.Application.Features.DataPortability;

/// <summary>
/// Versions the <see cref="BackupDocument"/> wire format. Bump
/// <see cref="Current"/> whenever a column is added, removed, or its meaning
/// changes — the import handler refuses any document whose
/// <see cref="BackupDocument.SchemaVersion"/> doesn't equal <see cref="Current"/>.
/// </summary>
public static class BackupSchemaVersion
{
    /// <summary>
    /// <list type="bullet">
    /// <item>v4 added <c>category_patterns</c>.</item>
    /// <item>v5 added <c>loans</c> + <c>loan_payments</c>.</item>
    /// <item>
    /// v6 added <c>pools</c>, <c>pool_participants</c> and
    /// <c>pool_unit_events</c>. There is no upgrade path, so this bump
    /// PERMANENTLY invalidates every v5 backup file already on disk - re-export
    /// after deploying it.
    /// </item>
    /// </list>
    /// </summary>
    public const int Current = 6;
}
