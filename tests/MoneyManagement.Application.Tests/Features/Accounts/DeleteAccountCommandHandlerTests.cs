using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts.DeleteAccount;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Accounts;

public class DeleteAccountCommandHandlerTests
{
    private static readonly DateOnly OpeningDate = new(2026, 1, 15);
    private static readonly DateOnly TxDate = new(2026, 3, 10);
    private static readonly DateTime ClockNow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Account NewAccount(string name = "Cash MDL", string currency = "MDL", decimal opening = 0m)
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(opening, currency),
            OpeningDate,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction NewTransaction(Guid accountId, Guid? counterAccountId = null)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            TxDate,
            TransactionDirection.Income,
            new Money(100m, "MDL"),
            "row",
            TransactionSource.Manual,
            isTransfer: counterAccountId is not null,
            counterAccountId: counterAccountId,
            isAdjustment: false);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static ImportBatch NewImportBatch(Guid accountId)
    {
        Result<ImportBatch> result = ImportBatch.Create(
            accountId,
            fileName: "statement.csv",
            fileHash: "abc123",
            BankSource.Maib,
            importedAt: ClockNow,
            importedCount: 5,
            skippedDuplicateCount: 0);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static SavingsGoal NewLinkedGoal(Guid linkedAccountId)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(ClockNow);

        Result<SavingsGoal> result = SavingsGoal.Create(
            name: "Emergency fund",
            targetAmount: new Money(10_000m, "MDL"),
            targetDate: null,
            linkedAccountId: linkedAccountId,
            clock);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithEmptyAccount_RemovesAndSucceeds()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new DeleteAccountCommandHandler(db);

        Result result = await handler.Handle(new DeleteAccountCommand(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.Accounts.Should().NotContain(account);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithArchivedEmptyAccount_RemovesAndSucceeds()
    {
        Account account = NewAccount();
        account.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new DeleteAccountCommandHandler(db);

        Result result = await handler.Handle(new DeleteAccountCommand(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.Accounts.Should().NotContain(account);
    }

    [Fact]
    public async Task Handle_WithTransactions_ReturnsConflict()
    {
        Account account = NewAccount();
        Transaction transaction = NewTransaction(account.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [transaction]);

        var handler = new DeleteAccountCommandHandler(db);

        Result result = await handler.Handle(new DeleteAccountCommand(account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.HasLinkedRecords(account.Id));
        db.Accounts.Should().Contain(account);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithCounterAccountTransaction_ReturnsConflict()
    {
        Account account = NewAccount();
        // Transaction belongs to another account but references this one as the
        // counter account leg of a transfer.
        Transaction transfer = NewTransaction(Guid.NewGuid(), counterAccountId: account.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [transfer]);

        var handler = new DeleteAccountCommandHandler(db);

        Result result = await handler.Handle(new DeleteAccountCommand(account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.HasLinkedRecords(account.Id));
        db.Accounts.Should().Contain(account);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithImportBatch_ReturnsConflict()
    {
        Account account = NewAccount();
        ImportBatch batch = NewImportBatch(account.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            imports: [batch]);

        var handler = new DeleteAccountCommandHandler(db);

        Result result = await handler.Handle(new DeleteAccountCommand(account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.HasLinkedRecords(account.Id));
        db.Accounts.Should().Contain(account);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithLinkedGoal_ReturnsConflict()
    {
        Account account = NewAccount();
        SavingsGoal goal = NewLinkedGoal(account.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        var handler = new DeleteAccountCommandHandler(db);

        Result result = await handler.Handle(new DeleteAccountCommand(account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.HasLinkedRecords(account.Id));
        db.Accounts.Should().Contain(account);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingAccount_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new DeleteAccountCommandHandler(db);

        var unknownId = Guid.NewGuid();
        Result result = await handler.Handle(new DeleteAccountCommand(unknownId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
