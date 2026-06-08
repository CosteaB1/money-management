using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.DataPortability;

/// <summary>
/// Application-level errors for the Data export / import slice. Lives in
/// Application (not Domain) because the slice has no entity — it's a faithful
/// snapshot over every existing table, restored behind <c>IBackupStore</c>.
/// </summary>
public static class DataErrors
{
    public static Error UnsupportedSchemaVersion(int found) =>
        Error.Validation(
            "data.unsupported_schema_version",
            $"Backup schema version {found} is not supported. Expected version {BackupSchemaVersion.Current}.");

    public static Error MalformedBackup(string detail) =>
        Error.Validation("data.malformed_backup", detail);
}
