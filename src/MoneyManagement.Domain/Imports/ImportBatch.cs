using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Imports;

public sealed class ImportBatch : Entity
{
    public const int FileNameMaxLength = 260;
    public const int FileHashMaxLength = 64;

    // EF Core
    private ImportBatch() { }

    private ImportBatch(
        Guid id,
        Guid accountId,
        string fileName,
        string fileHash,
        BankSource bankSource,
        DateTime importedAt,
        int importedCount,
        int skippedDuplicateCount) : base(id)
    {
        AccountId = accountId;
        FileName = fileName;
        FileHash = fileHash;
        BankSource = bankSource;
        ImportedAt = importedAt;
        ImportedCount = importedCount;
        SkippedDuplicateCount = skippedDuplicateCount;
    }

    public Guid AccountId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string FileHash { get; private set; } = string.Empty;
    public BankSource BankSource { get; private set; }
    public DateTime ImportedAt { get; private set; }
    public int ImportedCount { get; private set; }
    public int SkippedDuplicateCount { get; private set; }

    public static Result<ImportBatch> Create(
        Guid accountId,
        string fileName,
        string fileHash,
        BankSource bankSource,
        DateTime importedAt,
        int importedCount,
        int skippedDuplicateCount)
    {
        if (accountId == Guid.Empty)
        {
            return Result.Failure<ImportBatch>(ImportBatchErrors.AccountRequired);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result.Failure<ImportBatch>(ImportBatchErrors.FileNameRequired);
        }

        if (fileName.Length > FileNameMaxLength)
        {
            return Result.Failure<ImportBatch>(ImportBatchErrors.FileNameTooLong);
        }

        if (string.IsNullOrWhiteSpace(fileHash))
        {
            return Result.Failure<ImportBatch>(ImportBatchErrors.FileHashRequired);
        }

        if (fileHash.Length > FileHashMaxLength)
        {
            return Result.Failure<ImportBatch>(ImportBatchErrors.FileHashTooLong);
        }

        return new ImportBatch(
            Guid.CreateVersion7(),
            accountId,
            fileName,
            fileHash,
            bankSource,
            importedAt,
            importedCount,
            skippedDuplicateCount);
    }
}
