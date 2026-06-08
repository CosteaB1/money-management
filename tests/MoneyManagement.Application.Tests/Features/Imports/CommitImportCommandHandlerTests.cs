using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Application.Features.Imports.CommitImport;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Imports;

public class CommitImportCommandHandlerTests
{
    private static readonly DateOnly TxDate = new(2026, 4, 10);

    private static Account NewAccount(string name = "Salary", string currency = "MDL")
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(0m, currency),
            new DateOnly(2026, 1, 1),
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Category NewCategory(string name = "Groceries")
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static TransactionToImport SimpleExpense(
        decimal amount = 30m,
        string description = "Coffee at Linella") =>
        new(
            TransactionDate: TxDate,
            Direction: TransactionDirection.Expense,
            Amount: amount,
            Description: description,
            CategoryId: null,
            OriginalAmount: null,
            OriginalCurrency: null,
            IsTransfer: false);

    private static IDateTimeProvider FixedClock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static Transaction NewTransferLeg(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        string description,
        DateOnly? date = null,
        string currency = "MDL")
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date ?? TxDate,
            direction,
            new Money(amount, currency),
            description,
            TransactionSource.Imported,
            categoryId: null,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: true,
            counterAccountId: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_TransferRowWithoutCounterAccount_CreatesSingleLeg()
    {
        // Counter account is OPTIONAL: a transfer row with no counter picked
        // imports as a single leg with is_transfer = true, counter = null.
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "A2A de iesire pe cardul 435696***5875",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: null),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);
        result.Value.SkippedDuplicates.Should().Be(0);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(1);

        Transaction primary = persisted[0];
        primary.AccountId.Should().Be(account.Id);
        primary.IsTransfer.Should().BeTrue();
        primary.CounterAccountId.Should().BeNull();
        primary.Direction.Should().Be(TransactionDirection.Expense);
        primary.Source.Should().Be(TransactionSource.Imported);
    }

    [Fact]
    public async Task Handle_RowWithNotes_PersistsNotesOnBothLegs()
    {
        // The frontend may attach an optional per-row note. It is mirrored onto
        // BOTH the source row and the derived counter leg of a transfer, so the
        // same annotation is visible from either account.
        Account source = NewAccount("Salary");
        Account counter = NewAccount("Cash");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, counter]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "ATM withdrawal",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id,
                    Notes: "Paid in cash"),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);

        Transaction primary = persisted.Single(t => t.AccountId == source.Id);
        primary.Notes.Should().Be("Paid in cash");

        Transaction matching = persisted.Single(t => t.AccountId == counter.Id);
        matching.Notes.Should().Be("Paid in cash");
    }

    [Fact]
    public async Task Handle_NonTransferRow_PersistsSingleLeg()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 30m,
                    Description: "Coffee at Linella",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: false),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(1);
        persisted[0].IsTransfer.Should().BeFalse();
        persisted[0].CounterAccountId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransferRowWithCounterAccount_CreatesBothLegs()
    {
        // Happy path: transfer row with a counter picked. The handler creates
        // a primary leg on the import account AND a matching leg on the
        // counter account, both flagged is_transfer, cross-linked via
        // counter_account_id, and sharing the same import-batch id.
        Account source = NewAccount("Salary");
        Account counter = NewAccount("Cash");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, counter]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "ATM withdrawal",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // imported_count is user-facing preview rows, not paired-leg multiplicity.
        result.Value.ImportedCount.Should().Be(1);
        result.Value.SkippedDuplicates.Should().Be(0);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);

        Transaction primary = persisted.Single(t => t.AccountId == source.Id);
        Transaction matching = persisted.Single(t => t.AccountId == counter.Id);

        primary.IsTransfer.Should().BeTrue();
        primary.CounterAccountId.Should().Be(counter.Id);
        primary.Direction.Should().Be(TransactionDirection.Expense);

        matching.IsTransfer.Should().BeTrue();
        matching.CounterAccountId.Should().Be(source.Id);
        matching.Direction.Should().Be(TransactionDirection.Income);
        matching.Amount.Amount.Should().Be(500m);
        matching.Description.Should().Be("ATM withdrawal");

        primary.ImportBatchId.Should().NotBeNull();
        primary.ImportBatchId.Should().Be(matching.ImportBatchId);
        primary.ImportBatchId.Should().Be(result.Value.ImportBatchId);
    }

    [Fact]
    public async Task Handle_CounterAccountSameAsImportAccount_Fails()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "Self transfer",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: account.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.CounterAccountCannotBeSelf);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CrossCurrencyCounterAccountWithCounterAmount_CreatesCounterLegInOwnCurrency()
    {
        // MDL row flagged as transfer to a USD counter account, with an explicit
        // CounterAmount. The counter leg is denominated in USD (1000), keeps the
        // row's source-derived MDL value, and cross-stamps Original* with the
        // source row's amount+currency.
        Account source = NewAccount("MAIB", currency: "MDL");
        Account counter = NewAccount("Bybit", currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, counter]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 17_163m,
                    Description: "FX transfer to Bybit",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id,
                    CounterAmount: 1_000m),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);

        Transaction primary = persisted.Single(t => t.AccountId == source.Id);
        primary.Amount.Amount.Should().Be(17_163m);
        primary.Amount.Currency.Should().Be("MDL");

        Transaction matching = persisted.Single(t => t.AccountId == counter.Id);
        matching.Direction.Should().Be(TransactionDirection.Income);
        matching.Amount.Amount.Should().Be(1_000m);
        matching.Amount.Currency.Should().Be("USD");
        matching.CounterAccountId.Should().Be(source.Id);
        // Cross-stamp: counter leg carries the source row's amount+currency.
        matching.OriginalAmount.Should().Be(17_163m);
        matching.OriginalCurrency.Should().Be("MDL");
    }

    [Fact]
    public async Task Handle_CrossCurrencyCounterAccountWithoutCounterAmount_Fails()
    {
        Account source = NewAccount("MAIB", currency: "MDL");
        Account counter = NewAccount("Bybit", currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, counter]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "FX transfer (no counter amount)",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.CounterAmountRequired);
    }

    [Fact]
    public async Task Handle_CounterAccountArchived_Fails()
    {
        Account source = NewAccount("Salary");
        Account counter = NewAccount("Closed Wallet");
        counter.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, counter]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "Transfer to archived",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.IsArchived(counter.Id));

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CounterAccountNotFound_Fails()
    {
        Account source = NewAccount("Salary");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var missingCounterId = Guid.CreateVersion7();
        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "Transfer to ghost",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: missingCounterId),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingCounterId));

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TransferRowMatchesExistingTransferLeg_FlagsAsDuplicate()
    {
        // Scenario: the source account (Salary) was imported first and -- via
        // the matching-leg machinery -- created an Income leg on the destination
        // account (Checking) with description "A2A de iesire ...". The user then
        // imports the Checking account's PDF, where the same transfer appears
        // with description "A2A de intrare ...". Description-based dedup misses
        // it, so the transfer-aware fallback kicks in and flags the row as duplicate.
        Account account = NewAccount("Checking");
        Transaction existingLeg = NewTransferLeg(
            account.Id,
            TransactionDirection.Income,
            500m,
            "A2A de iesire pe cardul 435696***5875");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existingLeg]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Income,
                    Amount: 500m,
                    Description: "A2A de intrare pe cardul 999999***0000",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(0);
        result.Value.SkippedDuplicates.Should().Be(1);

        // No new transactions inserted; pre-existing leg is the only row.
        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().ContainSingle()
            .Which.Id.Should().Be(existingLeg.Id);
    }

    [Fact]
    public async Task Handle_NonTransferRowSameDateAndAmount_NotDeduped()
    {
        // Scenario: an existing non-transfer transaction shares the same date,
        // amount, and direction with an incoming transfer row. The transfer-aware
        // fallback must not fire because the existing row is not a transfer.
        Account account = NewAccount();
        Result<Transaction> existingResult = Transaction.Create(
            account.Id,
            TxDate,
            TransactionDirection.Income,
            new Money(500m, "MDL"),
            "Salary advance",
            TransactionSource.Imported,
            categoryId: null,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: false,
            counterAccountId: null);

        existingResult.IsSuccess.Should().BeTrue();
        Transaction existing = existingResult.Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existing]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Income,
                    Amount: 500m,
                    Description: "A2A de intrare pe cardul 999999***0000",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);
        result.Value.SkippedDuplicates.Should().Be(0);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_TwoIdenticalRowsInBatch_BothPersisted()
    {
        // Two rows with identical (date, amount, description) -- mirrors a real
        // maib pattern where the same ATM is hit twice on the same day for the
        // same amount, or two A2A transfers fire for the same nominal amount.
        // They are NOT duplicates of each other; the parser emits both because
        // the statement actually contains both. Regression for a bug where
        // HashSet<T>.Add was being used during the dedup loop, silently
        // dropping the second row.
        var date = new DateOnly(2025, 8, 22);
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: date,
                    Direction: TransactionDirection.Expense,
                    Amount: 8000m,
                    Description: "ATM MAIB REC IALOVENI",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: false,
                    CounterAccountId: null),
                new TransactionToImport(
                    TransactionDate: date,
                    Direction: TransactionDirection.Expense,
                    Amount: 8000m,
                    Description: "ATM MAIB REC IALOVENI",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: false,
                    CounterAccountId: null),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(2);
        result.Value.SkippedDuplicates.Should().Be(0);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);
        persisted.Should().AllSatisfy(t =>
        {
            t.AccountId.Should().Be(account.Id);
            t.TransactionDate.Should().Be(date);
            t.Amount.Amount.Should().Be(8000m);
            t.Description.Should().Be("ATM MAIB REC IALOVENI");
        });
    }

    [Fact]
    public async Task Handle_TwoIdenticalTransferRowsInBatch_BothLegsPairsPersisted()
    {
        // Same regression, but for the counter-side dedup: two identical
        // transfer rows in one batch must each get their own matching leg on
        // the counter account. Previously the per-counter HashSet was mutated
        // inside the loop, so the second row's matching leg was suppressed.
        var date = new DateOnly(2025, 11, 20);
        Account source = NewAccount("Salary");
        Account counter = NewAccount("Checking");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, counter]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: source.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: date,
                    Direction: TransactionDirection.Expense,
                    Amount: 5000m,
                    Description: "A2A de iesire pe cardul 435696***5875",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id),
                new TransactionToImport(
                    TransactionDate: date,
                    Direction: TransactionDirection.Expense,
                    Amount: 5000m,
                    Description: "A2A de iesire pe cardul 435696***5875",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(2);
        result.Value.SkippedDuplicates.Should().Be(0);

        List<Transaction> persisted = [.. db.Transactions];
        // Two primaries on source + two matching legs on counter = 4 rows.
        persisted.Should().HaveCount(4);
        persisted.Count(t => t.AccountId == source.Id).Should().Be(2);
        persisted.Count(t => t.AccountId == counter.Id).Should().Be(2);
        persisted.Where(t => t.AccountId == counter.Id).Should().AllSatisfy(t =>
        {
            t.Direction.Should().Be(TransactionDirection.Income);
            t.Amount.Amount.Should().Be(5000m);
            t.CounterAccountId.Should().Be(source.Id);
        });
    }

    [Fact]
    public async Task Handle_ReimportOfBatchWithTwoIdenticalRows_SkipsAll()
    {
        // Re-import safety net: when the DB already contains two identical
        // (date, amount, description) rows from a prior import, re-importing
        // the same statement must produce zero new inserts. The signature
        // snapshot is read-only and Contains returns true for both PDF rows.
        var date = new DateOnly(2025, 8, 22);
        Account account = NewAccount();

        Result<Transaction> existingA = Transaction.Create(
            account.Id,
            date,
            TransactionDirection.Expense,
            new Money(8000m, "MDL"),
            "ATM MAIB REC IALOVENI",
            TransactionSource.Imported,
            categoryId: null,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: false,
            counterAccountId: null);
        existingA.IsSuccess.Should().BeTrue();

        Result<Transaction> existingB = Transaction.Create(
            account.Id,
            date,
            TransactionDirection.Expense,
            new Money(8000m, "MDL"),
            "ATM MAIB REC IALOVENI",
            TransactionSource.Imported,
            categoryId: null,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: false,
            counterAccountId: null);
        existingB.IsSuccess.Should().BeTrue();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existingA.Value, existingB.Value]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: date,
                    Direction: TransactionDirection.Expense,
                    Amount: 8000m,
                    Description: "ATM MAIB REC IALOVENI",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: false,
                    CounterAccountId: null),
                new TransactionToImport(
                    TransactionDate: date,
                    Direction: TransactionDirection.Expense,
                    Amount: 8000m,
                    Description: "ATM MAIB REC IALOVENI",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: false,
                    CounterAccountId: null),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(0);
        result.Value.SkippedDuplicates.Should().Be(2);

        // DB still has only the two pre-existing rows; no new inserts.
        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_TransferRowDifferentDirection_NotDeduped()
    {
        // Direction must match too: an existing transfer leg with opposite
        // direction is a different real-world side of a different transfer.
        Account account = NewAccount();
        Transaction existingExpense = NewTransferLeg(
            account.Id,
            TransactionDirection.Expense,
            500m,
            "A2A de iesire pe cardul 435696***5875");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existingExpense]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Income,
                    Amount: 500m,
                    Description: "A2A de intrare pe cardul 999999***0000",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_LearnedPatterns_InsertsNewLearnedRows_KeywordUpperCased()
    {
        Account account = NewAccount();
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            categories: [category]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()],
            LearnedPatterns:
            [
                new LearnedCategoryPattern("  linella  ", category.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<CategoryPattern> patterns = [.. db.CategoryPatterns];
        patterns.Should().ContainSingle();
        CategoryPattern learned = patterns[0];
        learned.Keyword.Should().Be("LINELLA");
        learned.CategoryId.Should().Be(category.Id);
        learned.Source.Should().Be(CategoryPatternSource.Learned);
    }

    [Fact]
    public async Task Handle_LearnedPatternAlreadyExists_SkipsWithoutDuplicateOrError()
    {
        Account account = NewAccount();
        Category category = NewCategory();

        // Pre-existing rule (stored upper-cased) pointing at the same category.
        Result<CategoryPattern> existing = CategoryPattern.Create(
            "LINELLA",
            category.Id,
            CategoryPatternSource.Seeded);
        existing.IsSuccess.Should().BeTrue();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            categories: [category],
            categoryPatterns: [existing.Value]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()],
            LearnedPatterns:
            [
                // Same keyword (different casing) -- must not clobber or duplicate.
                new LearnedCategoryPattern("linella", category.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<CategoryPattern> patterns = [.. db.CategoryPatterns];
        patterns.Should().ContainSingle();
        patterns[0].Id.Should().Be(existing.Value.Id);
        patterns[0].Source.Should().Be(CategoryPatternSource.Seeded);
    }

    [Fact]
    public async Task Handle_BlankLearnedKeyword_Skipped()
    {
        Account account = NewAccount();
        Category category = NewCategory();
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            categories: [category]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()],
            LearnedPatterns:
            [
                new LearnedCategoryPattern("   ", category.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<CategoryPattern> patterns = [.. db.CategoryPatterns];
        patterns.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_LearnedPatternUnknownCategory_Skipped_ImportStillSucceeds()
    {
        Account account = NewAccount();
        // No categories seeded -> the learned categoryId resolves to nothing.
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()],
            LearnedPatterns:
            [
                new LearnedCategoryPattern("LINELLA", Guid.CreateVersion7()),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImportedCount.Should().Be(1);

        List<CategoryPattern> patterns = [.. db.CategoryPatterns];
        patterns.Should().BeEmpty();

        // Import itself is unaffected by the skipped pattern.
        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_MissingAccount_ReturnsAccountNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var missingAccount = Guid.CreateVersion7();
        var command = new CommitImportCommand(
            AccountId: missingAccount,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingAccount));
    }

    [Fact]
    public async Task Handle_EmptyTransactionList_ReturnsEmptyBatch()
    {
        // Account exists but no rows — the validator shadows this in the pipeline,
        // so the handler's own empty_batch guard is exercised directly here.
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: []);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("imports.empty_batch");
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PrimaryLegFactoryFails_PropagatesError()
    {
        // A description over the domain max makes Transaction.Create fail for the
        // primary leg; the handler surfaces that error.
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                SimpleExpense(amount: 30m, description: new string('x', Transaction.DescriptionMaxLength + 1)),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.DescriptionTooLong);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ImportBatchFactoryFails_PropagatesError()
    {
        // A file name over the ImportBatch max makes ImportBatch.Create fail after
        // the transactions have been built; the handler surfaces that error.
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: new string('f', ImportBatch.FileNameMaxLength + 1),
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.FileNameTooLong);
    }

    [Fact]
    public async Task Handle_CounterLegMatchingExistingLeg_IsSkipped()
    {
        // The counter account already has a row matching (date, amount, opposite
        // direction), so the matching leg is skipped (only the primary leg lands).
        Account account = NewAccount("Salary");
        Account counter = NewAccount("Daily");
        // Pre-existing income leg on the counter that matches the incoming transfer.
        Transaction existingLeg = NewTransferLeg(
            counter.Id, TransactionDirection.Income, 500m, "A2A de intrare", TxDate);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account, counter],
            transactions: [existingLeg]);
        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions:
            [
                new TransactionToImport(
                    TransactionDate: TxDate,
                    Direction: TransactionDirection.Expense,
                    Amount: 500m,
                    Description: "A2A de iesire pe cardul",
                    CategoryId: null,
                    OriginalAmount: null,
                    OriginalCurrency: null,
                    IsTransfer: true,
                    CounterAccountId: counter.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Started with 1 pre-existing counter leg; only the primary leg is added
        // (the matching counter leg is skipped as a duplicate) -> 2 rows total.
        db.Transactions.Should().HaveCount(2);
        db.Transactions.Count(t => t.AccountId == account.Id).Should().Be(1);
        db.Transactions.Count(t => t.AccountId == counter.Id).Should().Be(1);
    }

    [Fact]
    public async Task Handle_LearnedPatternWithInvalidKeyword_IsSkipped_NotFatal()
    {
        // A learned keyword that is too long fails CategoryPattern.Create; the
        // handler skips it (non-fatal) and still commits the transactions.
        Account account = NewAccount();
        Category groceries = NewCategory("Groceries");
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            categories: [groceries]);
        var handler = new CommitImportCommandHandler(db, FixedClock(), FakeFxConverter.Identity());

        var command = new CommitImportCommand(
            AccountId: account.Id,
            FileName: "statement.pdf",
            FileHash: "abc123",
            BankSource: BankSource.Maib,
            Transactions: [SimpleExpense()],
            LearnedPatterns:
            [
                new LearnedCategoryPattern(new string('k', CategoryPattern.KeywordMaxLength + 1), groceries.Id),
            ]);

        Result<CommitResultDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.CategoryPatterns.Should().BeEmpty();
        db.Transactions.Should().ContainSingle();
    }
}
