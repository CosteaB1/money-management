using FluentAssertions;
using MoneyManagement.Application.Abstractions.Backup;
using MoneyManagement.Application.Features.DataPortability;
using MoneyManagement.Application.Features.DataPortability.ExportData;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.DataPortability;

public class ExportDataQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsDocumentFromStore()
    {
        BackupDocument document = EmptyDocument() with
        {
            Accounts =
            [
                new AccountBackup(
                    Guid.CreateVersion7(),
                    "Wallet",
                    Domain.Accounts.AccountType.Cash,
                    100m,
                    "MDL",
                    new DateOnly(2026, 1, 1),
                    IsArchived: false,
                    Notes: null,
                    CreatedAt: DateTime.UtcNow,
                    UpdatedAt: DateTime.UtcNow),
            ],
        };

        IBackupStore store = Substitute.For<IBackupStore>();
        store.ExportAsync(Arg.Any<CancellationToken>()).Returns(document);

        var handler = new ExportDataQueryHandler(store);

        Result<BackupDocument> result = await handler.Handle(new ExportDataQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(document);
        result.Value.Accounts.Should().HaveCount(1);
        await store.Received(1).ExportAsync(Arg.Any<CancellationToken>());
    }

    private static BackupDocument EmptyDocument() => new(
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
