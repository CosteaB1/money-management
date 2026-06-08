using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class AdjustBalanceCommandHandlerTests
{
    private static readonly DateOnly OpeningDate = new(2026, 1, 1);
    private static readonly DateOnly AdjustmentDate = new(2026, 4, 30);

    private static Account NewAccount(
        AccountType type = AccountType.Brokerage,
        string currency = "USD",
        decimal opening = 1_000m,
        string name = "XTB")
    {
        Result<Account> result = Account.Create(
            name,
            type,
            new Money(opening, currency),
            OpeningDate,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_PositiveDelta_PersistsIncomeAdjustmentTransaction()
    {
        Account account = NewAccount(type: AccountType.Brokerage, opening: 1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(account.Id, BalanceChangeKind.Adjustment, Value: 1_250m, AdjustmentDate, Notes: "Quarterly mark");

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(250m);

        Transaction persisted = db.Transactions.Single();
        persisted.AccountId.Should().Be(account.Id);
        persisted.Direction.Should().Be(TransactionDirection.Income);
        persisted.Amount.Amount.Should().Be(250m);
        persisted.Amount.Currency.Should().Be("USD");
        persisted.IsAdjustment.Should().BeTrue();
        persisted.IsTransfer.Should().BeFalse();
        persisted.Source.Should().Be(TransactionSource.Manual);
        // Description stays the default label; the user's text is the NOTES.
        persisted.Description.Should().Be("Balance adjustment");
        persisted.Notes.Should().Be("Quarterly mark");
        persisted.CategoryId.Should().Be(SeededCategories.BalanceAdjustmentId);
        persisted.TransactionDate.Should().Be(AdjustmentDate);
        result.Value.TransactionId.Should().Be(persisted.Id);
    }

    [Fact]
    public async Task Handle_NegativeDelta_PersistsExpenseAdjustmentWithDefaultDescription()
    {
        Account account = NewAccount(type: AccountType.CryptoExchange, opening: 5_000m, currency: "USD", name: "Binance");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(account.Id, BalanceChangeKind.Adjustment, Value: 4_200m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(-800m);

        Transaction persisted = db.Transactions.Single();
        persisted.Direction.Should().Be(TransactionDirection.Expense);
        persisted.Amount.Amount.Should().Be(800m);
        persisted.Description.Should().Be("Balance adjustment");
        persisted.IsAdjustment.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingTransactionsAffectCurrentBalance()
    {
        Account account = NewAccount(type: AccountType.P2PLending, opening: 1_000m, currency: "MDL");

        Transaction priorInterest = Transaction.Create(
            account.Id,
            new DateOnly(2026, 3, 31),
            TransactionDirection.Income,
            new Money(50m, "MDL"),
            "Interest paid",
            TransactionSource.Manual).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [priorInterest]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        // Current balance = 1000 + 50 = 1050. Target = 1100 => delta = 50.
        var command = new AdjustBalanceCommand(account.Id, BalanceChangeKind.Adjustment, Value: 1_100m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_MissingAccount_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var missingId = Guid.CreateVersion7();
        var command = new AdjustBalanceCommand(missingId, BalanceChangeKind.Adjustment, Value: 100m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
        db.Transactions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccountType.Cash, BalanceChangeKind.Adjustment)]
    [InlineData(AccountType.CreditCard, BalanceChangeKind.Adjustment)]
    [InlineData(AccountType.BankCurrent, BalanceChangeKind.Adjustment)]
    [InlineData(AccountType.Cash, BalanceChangeKind.Investment)]
    [InlineData(AccountType.CreditCard, BalanceChangeKind.Withdrawal)]
    [InlineData(AccountType.BankCurrent, BalanceChangeKind.Investment)]
    public async Task Handle_IneligibleAccountType_ReturnsValidationError(
        AccountType type,
        BalanceChangeKind kind)
    {
        Account account = NewAccount(type: type, currency: "MDL", opening: 0m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(account.Id, kind, Value: 500m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("transaction.adjustment_account_type_not_eligible");
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownKind_ReturnsInvalidDirection()
    {
        // An out-of-range Kind passes the account-type gate (eligibility is by
        // account type only) and falls through the switch's default arm.
        Account account = NewAccount(type: AccountType.Brokerage, opening: 1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(
            account.Id, (BalanceChangeKind)999, Value: 100m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.InvalidDirection);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvestmentWithZeroValue_PropagatesTransactionFactoryFailure()
    {
        // A resolved BalanceChange with a non-positive amount makes
        // Transaction.Create fail; the handler must surface that error (the
        // command validator normally blocks Value <= 0 for non-Adjustment kinds).
        Account account = NewAccount(type: AccountType.Brokerage, opening: 1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(
            account.Id, BalanceChangeKind.Investment, Value: 0m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.AmountNotPositive);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ZeroDelta_ReturnsValidationError()
    {
        Account account = NewAccount(type: AccountType.BankDeposit, opening: 10_000m, currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(account.Id, BalanceChangeKind.Adjustment, Value: 10_000m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.AdjustmentDeltaZero);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BankFeesCategoryTransactions_AreIncludedInCurrentBalance()
    {
        // Regression for the maib "Sold Disponibil" alignment: the handler
        // must use the same fee-inclusive balance definition as
        // GetAccountsQueryHandler. The maib parser splits its combined
        // `ieșiri` column into principal + fee at the source, so fees are
        // real, already-deducted money. Summing them here keeps the delta
        // aligned with the balance the user sees.
        Account account = NewAccount(type: AccountType.Brokerage, opening: 1_000m, currency: "MDL");

        Transaction bankFee = Transaction.Create(
            account.Id,
            new DateOnly(2026, 3, 31),
            TransactionDirection.Expense,
            new Money(25m, "MDL"),
            "Comision: account fee",
            TransactionSource.Manual,
            categoryId: SeededCategories.BankFeesId).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [bankFee]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        // Fee-inclusive current balance = 1000 - 25 = 975.
        // Target = 1100 => delta = 125.
        var command = new AdjustBalanceCommand(account.Id, BalanceChangeKind.Adjustment, Value: 1_100m, AdjustmentDate, Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(125m);
    }

    [Fact]
    public async Task Handle_Investment_PersistsTransferFlaggedIncomeWithInvestmentCategory()
    {
        Account account = NewAccount(type: AccountType.Brokerage, opening: 1_000m, currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(
            account.Id,
            BalanceChangeKind.Investment,
            Value: 500m,
            AdjustmentDate,
            Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(500m);

        Transaction persisted = db.Transactions.Single();
        persisted.Direction.Should().Be(TransactionDirection.Income);
        persisted.Amount.Amount.Should().Be(500m);
        persisted.Amount.Currency.Should().Be("USD");
        persisted.CategoryId.Should().Be(SeededCategories.InvestmentId);
        persisted.IsTransfer.Should().BeTrue();
        persisted.IsAdjustment.Should().BeFalse();
        persisted.CounterAccountId.Should().BeNull();
        persisted.Source.Should().Be(TransactionSource.Manual);
        persisted.Description.Should().Be("Investment");
        result.Value.TransactionId.Should().Be(persisted.Id);
    }

    [Fact]
    public async Task Handle_Investment_StoresNotesAsNotes_KeepsDefaultDescription()
    {
        Account account = NewAccount(type: AccountType.CryptoExchange, opening: 0m, currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(
            account.Id,
            BalanceChangeKind.Investment,
            Value: 250m,
            AdjustmentDate,
            Notes: "Top-up from salary");

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction persisted = db.Transactions.Single();
        // Description stays the default kind label; the free text is the NOTES.
        persisted.Description.Should().Be("Investment");
        persisted.Notes.Should().Be("Top-up from salary");
    }

    [Fact]
    public async Task Handle_Withdrawal_PersistsTransferFlaggedExpenseWithWithdrawalCategory()
    {
        Account account = NewAccount(type: AccountType.P2PLending, opening: 1_000m, currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(
            account.Id,
            BalanceChangeKind.Withdrawal,
            Value: 300m,
            AdjustmentDate,
            Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(-300m);

        Transaction persisted = db.Transactions.Single();
        persisted.Direction.Should().Be(TransactionDirection.Expense);
        persisted.Amount.Amount.Should().Be(300m);
        persisted.Amount.Currency.Should().Be("MDL");
        persisted.CategoryId.Should().Be(SeededCategories.WithdrawalId);
        persisted.IsTransfer.Should().BeTrue();
        persisted.IsAdjustment.Should().BeFalse();
        persisted.CounterAccountId.Should().BeNull();
        persisted.Source.Should().Be(TransactionSource.Manual);
        persisted.Description.Should().Be("Withdrawal");
        result.Value.TransactionId.Should().Be(persisted.Id);
    }

    [Fact]
    public async Task Handle_InvestmentAndWithdrawal_IgnoreExistingBalanceWhenSizingTheMove()
    {
        // Unlike Adjustment, Investment/Withdrawal take the supplied amount
        // verbatim - prior transactions on the account don't affect the delta.
        Account account = NewAccount(type: AccountType.BankDeposit, opening: 10_000m, currency: "MDL");

        Transaction priorInterest = Transaction.Create(
            account.Id,
            new DateOnly(2026, 3, 31),
            TransactionDirection.Income,
            new Money(750m, "MDL"),
            "Interest",
            TransactionSource.Manual).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [priorInterest]);

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity());
        var command = new AdjustBalanceCommand(
            account.Id,
            BalanceChangeKind.Withdrawal,
            Value: 400m,
            AdjustmentDate,
            Notes: null);

        Result<AdjustBalanceResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delta.Should().Be(-400m);
        db.Transactions.Count().Should().Be(2);
    }
}
