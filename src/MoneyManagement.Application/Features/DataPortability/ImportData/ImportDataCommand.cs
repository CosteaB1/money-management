using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.DataPortability.ImportData;

/// <summary>
/// Destructive full-replace restore. The handler validates the document's
/// schema version and structural soundness, then hands it to
/// <c>IBackupStore.RestoreAsync</c> which wipes and reinserts inside one
/// transaction.
/// </summary>
public sealed record ImportDataCommand(BackupDocument Document) : ICommand<ImportDataResult>;
