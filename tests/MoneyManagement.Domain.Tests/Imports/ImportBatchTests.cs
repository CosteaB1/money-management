using FluentAssertions;
using MoneyManagement.Domain.Imports;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Imports;

/// <summary>
/// Factory-level guards for <see cref="ImportBatch.Create"/>. These validations
/// are shadowed by the API-layer validator in normal flows, so they are
/// exercised directly here.
/// </summary>
public class ImportBatchTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();
    private static readonly DateTime ImportedAt = new(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Result<ImportBatch> Create(
        Guid? accountId = null,
        string fileName = "statement.pdf",
        string fileHash = "abc123") =>
        ImportBatch.Create(
            accountId ?? AccountId,
            fileName,
            fileHash,
            BankSource.Maib,
            ImportedAt,
            importedCount: 5,
            skippedDuplicateCount: 2);

    [Fact]
    public void Create_WithValidInput_ReturnsSuccess()
    {
        Result<ImportBatch> result = Create();

        result.IsSuccess.Should().BeTrue();
        ImportBatch batch = result.Value;
        batch.AccountId.Should().Be(AccountId);
        batch.FileName.Should().Be("statement.pdf");
        batch.FileHash.Should().Be("abc123");
        batch.BankSource.Should().Be(BankSource.Maib);
        batch.ImportedAt.Should().Be(ImportedAt);
        batch.ImportedCount.Should().Be(5);
        batch.SkippedDuplicateCount.Should().Be(2);
        batch.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WithEmptyAccountId_ReturnsAccountRequired()
    {
        Result<ImportBatch> result = Create(accountId: Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.AccountRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankFileName_ReturnsFileNameRequired(string fileName)
    {
        Result<ImportBatch> result = Create(fileName: fileName);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.FileNameRequired);
    }

    [Fact]
    public void Create_WithFileNameTooLong_ReturnsFileNameTooLong()
    {
        string fileName = new string('a', ImportBatch.FileNameMaxLength + 1);

        Result<ImportBatch> result = Create(fileName: fileName);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.FileNameTooLong);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankFileHash_ReturnsFileHashRequired(string fileHash)
    {
        Result<ImportBatch> result = Create(fileHash: fileHash);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.FileHashRequired);
    }

    [Fact]
    public void Create_WithFileHashTooLong_ReturnsFileHashTooLong()
    {
        string fileHash = new string('h', ImportBatch.FileHashMaxLength + 1);

        Result<ImportBatch> result = Create(fileHash: fileHash);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.FileHashTooLong);
    }

    [Fact]
    public void NotFound_BuildsNotFoundErrorCarryingTheId()
    {
        var id = Guid.CreateVersion7();

        Error error = ImportBatchErrors.NotFound(id);

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("import_batch.not_found");
        error.Message.Should().Contain(id.ToString());
    }
}
