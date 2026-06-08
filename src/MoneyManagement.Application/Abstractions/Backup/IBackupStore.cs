using MoneyManagement.Application.Features.DataPortability;

namespace MoneyManagement.Application.Abstractions.Backup;

/// <summary>
/// Faithful snapshot export + destructive full-replace restore over the whole
/// database. Defined in Application, implemented in Infrastructure
/// (<c>EfBackupStore</c>) — the wipe + reinsert needs <c>DbContext.Database</c>
/// (transactions, <c>ExecuteDeleteAsync</c>, raw INSERTs) which
/// <see cref="MoneyManagement.Application.Abstractions.Data.IApplicationDbContext"/>
/// deliberately does not expose. Mirrors the
/// <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/> split.
/// </summary>
public interface IBackupStore
{
    /// <summary>
    /// Reads every table (including archived rows and soft-deleted
    /// transactions) into a <see cref="BackupDocument"/>.
    /// </summary>
    Task<BackupDocument> ExportAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Destructively replaces ALL data with the contents of
    /// <paramref name="document"/> inside a single transaction. Wipes every
    /// table child-first, then reinserts parent-first with the document's
    /// EXACT original IDs / columns / audit fields. On any failure the
    /// transaction rolls back and the existing data is untouched. Returns the
    /// per-table inserted-row counts.
    /// </summary>
    Task<ImportDataResult> RestoreAsync(BackupDocument document, CancellationToken cancellationToken);
}
