using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Accounts.GetAccounts;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Accounts;

public class GetAccountsQueryHandlerTests
{
    private static readonly DateOnly OpeningDate = new(2026, 1, 15);
    private static readonly DateOnly TxDate = new(2026, 3, 10);

    private static Account NewAccount(string name, string currency, decimal opening)
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

    private static Transaction NewTransaction(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        string currency,
        bool isTransfer = false,
        Guid? counterAccountId = null,
        bool isAdjustment = false,
        Guid? categoryId = null)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            TxDate,
            direction,
            new Money(amount, currency),
            "test",
            TransactionSource.Manual,
            categoryId: categoryId,
            isTransfer: isTransfer,
            counterAccountId: counterAccountId,
            isAdjustment: isAdjustment);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_MdlAccount_PopulatesBalanceMdlAsIdentity()
    {
        Account account = NewAccount("Cash MDL", "MDL", 500m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        AccountDto dto = result.Value.Single();
        dto.Currency.Should().Be("MDL");
        dto.BalanceMdl.Should().Be(500m);
    }

    [Fact]
    public async Task Handle_UsdAccount_WithDirectRate_ConvertsToMdl()
    {
        Account account = NewAccount("USD wallet", "USD", 100m);
        FxRate rate = FxRate.Create("USD", "MDL", 17.50m, new DateOnly(2026, 1, 1)).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            fxRates: [rate]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.BalanceMdl.Should().Be(1_750m);
    }

    [Fact]
    public async Task Handle_AccountWithoutAvailableRate_ReturnsNullMdlValue()
    {
        Account account = NewAccount("CHF stash", "CHF", 100m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.BalanceMdl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AccountWithFutureOnlyRate_ReturnsNullMdlValue()
    {
        // Today is before any known rate -> no usable conversion.
        Account account = NewAccount("USD wallet", "USD", 100m);
        FxRate futureRate = FxRate.Create("USD", "MDL", 17.50m, new DateOnly(2099, 1, 1)).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            fxRates: [futureRate]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.BalanceMdl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AccountWithNoTransactions_BalanceEqualsAnchorBalance()
    {
        Account account = NewAccount("Cash MDL", "MDL", 500m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.Balance.Should().Be(500m);
    }

    [Fact]
    public async Task Handle_IncomeAndExpenseTransactions_SumIntoBalance()
    {
        // Opening 1000 + 300 income - 120 expense = 1180.
        Account account = NewAccount("Cash MDL", "MDL", 1_000m);
        Transaction income = NewTransaction(account.Id, TransactionDirection.Income, 300m, "MDL");
        Transaction expense = NewTransaction(account.Id, TransactionDirection.Expense, 120m, "MDL");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [income, expense]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.Balance.Should().Be(1_180m);
    }

    [Fact]
    public async Task Handle_TransferTransactions_AreIncludedInBalance()
    {
        // Salary 5000 MDL transfers 1200 to Cash 0 MDL.
        // Salary expense leg: 5000 - 1200 = 3800.
        // Cash income leg: 0 + 1200 = 1200.
        Account salary = NewAccount("Salary", "MDL", 5_000m);
        Account cash = NewAccount("Cash", "MDL", 0m);

        Transaction transferOut = NewTransaction(
            salary.Id, TransactionDirection.Expense, 1_200m, "MDL",
            isTransfer: true, counterAccountId: cash.Id);
        Transaction transferIn = NewTransaction(
            cash.Id, TransactionDirection.Income, 1_200m, "MDL",
            isTransfer: true, counterAccountId: salary.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [salary, cash],
            transactions: [transferOut, transferIn]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto cashDto = result.Value.Single(a => a.Name == "Cash");
        AccountDto salaryDto = result.Value.Single(a => a.Name == "Salary");

        cashDto.Balance.Should().Be(1_200m);
        salaryDto.Balance.Should().Be(3_800m);
    }

    [Fact]
    public async Task Handle_SoftDeletedTransactions_AreExcludedFromBalance()
    {
        Account account = NewAccount("Cash MDL", "MDL", 1_000m);
        Transaction kept = NewTransaction(account.Id, TransactionDirection.Income, 200m, "MDL");
        Transaction deleted = NewTransaction(account.Id, TransactionDirection.Income, 500m, "MDL");
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [kept, deleted]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        // 1000 + 200 (kept) - deleted row's 500 must NOT be added = 1200.
        dto.Balance.Should().Be(1_200m);
    }

    [Fact]
    public async Task Handle_MdlAccount_BalanceMdlIsIdentity()
    {
        Account account = NewAccount("Cash MDL", "MDL", 1_000m);
        Transaction income = NewTransaction(account.Id, TransactionDirection.Income, 250m, "MDL");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [income]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.Balance.Should().Be(1_250m);
        dto.BalanceMdl.Should().Be(dto.Balance);
    }

    [Fact]
    public async Task Handle_UsdAccountWithRate_BalanceMdlConvertsAtTodaysRate()
    {
        // Opening 100 USD + 50 income - 30 expense = 120 USD. Rate 17.50 -> 2100 MDL.
        Account account = NewAccount("USD wallet", "USD", 100m);
        Transaction income = NewTransaction(account.Id, TransactionDirection.Income, 50m, "USD");
        Transaction expense = NewTransaction(account.Id, TransactionDirection.Expense, 30m, "USD");

        // Use a rate dated before any plausible test execution time so it
        // resolves against "today" inside the handler.
        FxRate rate = FxRate.Create("USD", "MDL", 17.50m, new DateOnly(2026, 1, 1)).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [income, expense],
            fxRates: [rate]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.Balance.Should().Be(120m);
        dto.BalanceMdl.Should().Be(2_100m);
    }

    [Fact]
    public async Task Handle_UsdAccountWithoutRate_BalanceMdlIsNull()
    {
        Account account = NewAccount("USD wallet", "USD", 100m);
        Transaction income = NewTransaction(account.Id, TransactionDirection.Income, 20m, "USD");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [income]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        dto.Balance.Should().Be(120m);
        dto.BalanceMdl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BankFeesCategoryTransactions_AreIncludedInBalance()
    {
        // The maib parser now splits the combined `ieșiri` column into a
        // principal row + a fee row whose sum equals the bank's actual
        // deduction. The live balance must therefore subtract fees like any
        // other expense — anything else would over-credit the account by the
        // total commissions across the period and drift from the bank's per-
        // row "Sold Disponibil".
        Account account = NewAccount("Cash MDL", "MDL", 1_000m);
        Transaction regularExpense = NewTransaction(
            account.Id, TransactionDirection.Expense, 50m, "MDL");
        Transaction bankFee = NewTransaction(
            account.Id,
            TransactionDirection.Expense,
            30m,
            "MDL",
            categoryId: SeededCategories.BankFeesId);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [regularExpense, bankFee]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        // 1000 - 50 (regular) - 30 (fee) = 920.
        dto.Balance.Should().Be(920m);
        // MDL identity for MDL account.
        dto.BalanceMdl.Should().Be(920m);
    }

    [Fact]
    public async Task Handle_UsdAccount_WithOnlyInverseRate_ConvertsViaInverse()
    {
        // No direct USD->MDL rate, only a MDL->USD rate. The handler must fall
        // back to the inverse: balance * (1 / rate).
        Account account = NewAccount("USD wallet", "USD", 100m);
        FxRate inverseRate = FxRate.Create("MDL", "USD", 0.05m, new DateOnly(2026, 1, 1)).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            fxRates: [inverseRate]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        // 100 USD * (1 / 0.05) = 2000 MDL.
        dto.BalanceMdl.Should().Be(2_000m);
    }
}
