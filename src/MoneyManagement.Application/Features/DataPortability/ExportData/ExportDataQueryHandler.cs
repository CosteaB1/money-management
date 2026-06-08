using MoneyManagement.Application.Abstractions.Backup;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.DataPortability.ExportData;

internal sealed class ExportDataQueryHandler(IBackupStore backupStore)
    : IQueryHandler<ExportDataQuery, BackupDocument>
{
    public async Task<Result<BackupDocument>> Handle(ExportDataQuery query, CancellationToken cancellationToken)
    {
        BackupDocument document = await backupStore.ExportAsync(cancellationToken);
        return Result.Success(document);
    }
}
