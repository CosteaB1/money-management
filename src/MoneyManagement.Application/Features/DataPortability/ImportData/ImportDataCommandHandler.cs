using MoneyManagement.Application.Abstractions.Backup;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.DataPortability.ImportData;

internal sealed class ImportDataCommandHandler(IBackupStore backupStore)
    : ICommandHandler<ImportDataCommand, ImportDataResult>
{
    public async Task<Result<ImportDataResult>> Handle(ImportDataCommand command, CancellationToken cancellationToken)
    {
        BackupDocument document = command.Document;

        if (document.SchemaVersion != BackupSchemaVersion.Current)
        {
            return Result.Failure<ImportDataResult>(DataErrors.UnsupportedSchemaVersion(document.SchemaVersion));
        }

        // A backup with any null entity array is structurally malformed — a
        // valid document always carries every table (an empty list, never null).
        // This guards the case where a hand-edited or partial JSON deserializes
        // with missing arrays.
        if (document.Accounts is null ||
            document.Categories is null ||
            document.CategoryPatterns is null ||
            document.Transactions is null ||
            document.ImportBatches is null ||
            document.Budgets is null ||
            document.BudgetPeriods is null ||
            document.SavingsGoals is null ||
            document.SavingsGoalContributions is null)
        {
            return Result.Failure<ImportDataResult>(
                DataErrors.MalformedBackup("One or more entity arrays are missing from the backup."));
        }

        ImportDataResult result = await backupStore.RestoreAsync(document, cancellationToken);
        return Result.Success(result);
    }
}
