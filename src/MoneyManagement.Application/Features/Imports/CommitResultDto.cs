namespace MoneyManagement.Application.Features.Imports;

public sealed record CommitResultDto(
    Guid ImportBatchId,
    int ImportedCount,
    int SkippedDuplicates);
