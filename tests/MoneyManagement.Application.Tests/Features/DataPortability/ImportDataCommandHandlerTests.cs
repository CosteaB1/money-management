using FluentAssertions;
using MoneyManagement.Application.Abstractions.Backup;
using MoneyManagement.Application.Features.DataPortability;
using MoneyManagement.Application.Features.DataPortability.ImportData;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.DataPortability;

public class ImportDataCommandHandlerTests
{
    [Fact]
    public async Task Handle_UnsupportedSchemaVersion_FailsWithoutTouchingStore()
    {
        IBackupStore store = Substitute.For<IBackupStore>();
        var handler = new ImportDataCommandHandler(store);

        BackupDocument document = ValidDocument() with { SchemaVersion = BackupSchemaVersion.Current + 1 };

        Result<ImportDataResult> result = await handler.Handle(
            new ImportDataCommand(document), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DataErrors.UnsupportedSchemaVersion(BackupSchemaVersion.Current + 1));
        await store.DidNotReceive().RestoreAsync(Arg.Any<BackupDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullEntityArray_FailsWithMalformedBackup()
    {
        IBackupStore store = Substitute.For<IBackupStore>();
        var handler = new ImportDataCommandHandler(store);

        // Simulate a partial / hand-edited JSON that deserialized with a null array.
        BackupDocument document = ValidDocument() with { Transactions = null! };

        Result<ImportDataResult> result = await handler.Handle(
            new ImportDataCommand(document), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("data.malformed_backup");
        await store.DidNotReceive().RestoreAsync(Arg.Any<BackupDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullCategoryPatternsArray_FailsWithMalformedBackup()
    {
        IBackupStore store = Substitute.For<IBackupStore>();
        var handler = new ImportDataCommandHandler(store);

        // category_patterns is a backed-up entity since schema v4; a doc missing
        // the array is malformed and must not reach the destructive restore.
        BackupDocument document = ValidDocument() with { CategoryPatterns = null! };

        Result<ImportDataResult> result = await handler.Handle(
            new ImportDataCommand(document), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("data.malformed_backup");
        await store.DidNotReceive().RestoreAsync(Arg.Any<BackupDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidDocument_CallsRestoreOnceAndReturnsCounts()
    {
        var counts = new ImportDataResult(
            Accounts: 2,
            Categories: 9,
            CategoryPatterns: 34,
            Transactions: 150,
            ImportBatches: 3,
            Budgets: 5,
            BudgetPeriods: 6,
            SavingsGoals: 1,
            SavingsGoalContributions: 7);

        IBackupStore store = Substitute.For<IBackupStore>();
        store.RestoreAsync(Arg.Any<BackupDocument>(), Arg.Any<CancellationToken>()).Returns(counts);

        var handler = new ImportDataCommandHandler(store);

        BackupDocument document = ValidDocument();

        Result<ImportDataResult> result = await handler.Handle(
            new ImportDataCommand(document), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(counts);
        await store.Received(1).RestoreAsync(document, Arg.Any<CancellationToken>());
    }

    private static BackupDocument ValidDocument() => new(
        BackupSchemaVersion.Current,
        DateTimeOffset.UtcNow,
        Accounts: [],
        Categories: [],
        CategoryPatterns: [],
        Transactions: [],
        ImportBatches: [],
        Budgets: [],
        BudgetPeriods: [],
        SavingsGoals: [],
        SavingsGoalContributions: []);
}
