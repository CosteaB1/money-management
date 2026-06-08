using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Reports.ExportTransactionsCsv;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Reports;

public class ExportTransactionsCsvQueryHandlerTests
{
    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    private static Account NewAccount(string name)
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(0m, "MDL"),
            LongAgo,
            notes: null);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Category NewCategory(string name)
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction Tx(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        string description,
        DateOnly date,
        Guid? categoryId = null,
        string currency = "MDL",
        bool isTransfer = false,
        bool isAdjustment = false,
        Guid? counterAccountId = null)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date,
            direction,
            new Money(amount, currency),
            description,
            TransactionSource.Manual,
            categoryId: categoryId,
            isTransfer: isTransfer,
            counterAccountId: counterAccountId,
            isAdjustment: isAdjustment);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static IFxConverter IdentityConverter()
    {
        IFxConverter fx = Substitute.For<IFxConverter>();
        fx.ConvertAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                decimal amount = call.ArgAt<decimal>(0);
                string from = call.ArgAt<string>(1);
                string to = call.ArgAt<string>(2);
                return Task.FromResult<decimal?>(
                    string.Equals(from, to, StringComparison.Ordinal) ? amount : null);
            });
        return fx;
    }

    [Fact]
    public async Task Handle_NoFilters_ReturnsAllNonDeletedRowsJoinedWithNames()
    {
        Account wallet = NewAccount("Wallet");
        Category food = NewCategory("Food");

        Transaction r1 = Tx(wallet.Id, TransactionDirection.Expense, 100m, "Linella", new DateOnly(2026, 3, 5), categoryId: food.Id);
        Transaction r2 = Tx(wallet.Id, TransactionDirection.Expense, 50m, "uncategorized", new DateOnly(2026, 3, 6));
        Transaction deleted = Tx(wallet.Id, TransactionDirection.Expense, 9_999m, "gone", new DateOnly(2026, 3, 7));
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            categories: [food],
            transactions: [r1, r2, deleted]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        TransactionExportRow linellaRow = result.Value.Single(r => r.Description == "Linella");
        linellaRow.AccountName.Should().Be("Wallet");
        linellaRow.CategoryName.Should().Be("Food");
        linellaRow.AmountMdl.Should().Be(100m);

        TransactionExportRow uncategorized = result.Value.Single(r => r.Description == "uncategorized");
        uncategorized.CategoryName.Should().Be(string.Empty);
    }

    [Fact]
    public async Task Handle_TransfersAndAdjustments_AreIncludedByDefault()
    {
        // CSV is caller-driven: by default we don't apply the P&L filter.
        Account wallet = NewAccount("Wallet");
        var counter = Guid.CreateVersion7();

        Transaction normal = Tx(wallet.Id, TransactionDirection.Expense, 100m, "normal", new DateOnly(2026, 3, 5));
        Transaction transfer = Tx(
            wallet.Id, TransactionDirection.Expense, 200m, "transfer", new DateOnly(2026, 3, 6),
            isTransfer: true, counterAccountId: counter);
        Transaction adjustment = Tx(
            wallet.Id, TransactionDirection.Income, 300m, "adjust", new DateOnly(2026, 3, 7),
            isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            transactions: [normal, transfer, adjustment]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(), CancellationToken.None);

        result.Value.Should().HaveCount(3);
        result.Value.Single(r => r.Description == "transfer").IsTransfer.Should().BeTrue();
        result.Value.Single(r => r.Description == "adjust").IsAdjustment.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_IsTransferFilter_RestrictsToMatchingRowsOnly()
    {
        Account wallet = NewAccount("Wallet");
        var counter = Guid.CreateVersion7();

        Transaction normal = Tx(wallet.Id, TransactionDirection.Expense, 100m, "normal", new DateOnly(2026, 3, 5));
        Transaction transfer = Tx(
            wallet.Id, TransactionDirection.Expense, 200m, "transfer", new DateOnly(2026, 3, 6),
            isTransfer: true, counterAccountId: counter);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            transactions: [normal, transfer]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(IsTransfer: false),
            CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Description.Should().Be("normal");
    }

    [Fact]
    public async Task Handle_DateRangeFilter_AppliesInclusively()
    {
        Account wallet = NewAccount("Wallet");

        Transaction before = Tx(wallet.Id, TransactionDirection.Expense, 1m, "before", new DateOnly(2026, 2, 28));
        Transaction inside = Tx(wallet.Id, TransactionDirection.Expense, 2m, "inside", new DateOnly(2026, 3, 15));
        Transaction after = Tx(wallet.Id, TransactionDirection.Expense, 3m, "after", new DateOnly(2026, 4, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            transactions: [before, inside, after]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 31)),
            CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Description.Should().Be("inside");
    }

    [Fact]
    public async Task Handle_CategoryIdFilter_RestrictsToMatchingRows()
    {
        Account wallet = NewAccount("Wallet");
        Category food = NewCategory("Food");

        Transaction inCat = Tx(wallet.Id, TransactionDirection.Expense, 10m, "linella", new DateOnly(2026, 3, 5), categoryId: food.Id);
        Transaction noCat = Tx(wallet.Id, TransactionDirection.Expense, 20m, "uncategorized", new DateOnly(2026, 3, 6));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            categories: [food],
            transactions: [inCat, noCat]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(CategoryId: food.Id),
            CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Description.Should().Be("linella");
    }

    [Fact]
    public async Task Handle_DirectionFilter_RestrictsToMatchingRows()
    {
        Account wallet = NewAccount("Wallet");

        Transaction expense = Tx(wallet.Id, TransactionDirection.Expense, 10m, "spent", new DateOnly(2026, 3, 5));
        Transaction income = Tx(wallet.Id, TransactionDirection.Income, 20m, "earned", new DateOnly(2026, 3, 6));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            transactions: [expense, income]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(Direction: TransactionDirection.Income),
            CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Description.Should().Be("earned");
    }

    [Fact]
    public async Task Handle_IsAdjustmentFilter_RestrictsToMatchingRows()
    {
        Account wallet = NewAccount("Wallet");

        Transaction normal = Tx(wallet.Id, TransactionDirection.Expense, 10m, "normal", new DateOnly(2026, 3, 5));
        Transaction adjustment = Tx(
            wallet.Id, TransactionDirection.Income, 20m, "adjust", new DateOnly(2026, 3, 6), isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [wallet],
            transactions: [normal, adjustment]);

        var handler = new ExportTransactionsCsvQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(IsAdjustment: true),
            CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Description.Should().Be("adjust");
    }
}
