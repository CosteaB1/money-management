using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.DataPortability.ExportData;

public sealed record ExportDataQuery : IQuery<BackupDocument>;
