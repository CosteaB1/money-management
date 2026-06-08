using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Common;
using MoneyManagement.Application.Features.Transactions;
using MoneyManagement.Application.Features.Transactions.GetTransactions;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class GetTransactionsQueryHandlerTests
{
    private static readonly Guid AccountA = Guid.CreateVersion7();
    private static readonly Guid AccountB = Guid.CreateVersion7();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Transaction Tx(
        Guid accountId,
        TransactionDirection direction,
        bool isTransfer,
        Guid? counterAccountId = null,
        string description = "row",
        string currency = "MDL",
        decimal amount = 100m,
        bool isAdjustment = false)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            Today,
            direction,
            new Money(amount, currency),
            description,
            TransactionSource.Manual,
            isTransfer: isTransfer,
            counterAccountId: counterAccountId,
            isAdjustment: isAdjustment);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithoutIsTransferFilter_ReturnsAllRows()
    {
        Transaction normal = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, description: "groceries");
        Transaction transfer = Tx(AccountA, TransactionDirection.Expense, isTransfer: true, counterAccountId: AccountB, description: "transfer out");
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [normal, transfer]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithIsTransferTrue_ReturnsOnlyTransfers()
    {
        Transaction normal = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, description: "groceries");
        Transaction transfer = Tx(AccountA, TransactionDirection.Expense, isTransfer: true, counterAccountId: AccountB, description: "transfer out");
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [normal, transfer]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(IsTransfer: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        TransactionDto only = result.Value.Items.Single();
        only.IsTransfer.Should().BeTrue();
        only.CounterAccountId.Should().Be(AccountB);
        only.Description.Should().Be("transfer out");
    }

    [Fact]
    public async Task Handle_WithIsTransferFalse_ExcludesTransfers()
    {
        Transaction normal = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, description: "groceries");
        Transaction transfer = Tx(AccountA, TransactionDirection.Expense, isTransfer: true, counterAccountId: AccountB, description: "transfer out");
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [normal, transfer]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(IsTransfer: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        TransactionDto only = result.Value.Items.Single();
        only.IsTransfer.Should().BeFalse();
        only.Description.Should().Be("groceries");
    }

    [Fact]
    public async Task Handle_WithIsAdjustmentTrue_ReturnsOnlyAdjustments()
    {
        Transaction normal = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, description: "groceries");
        Transaction adjustment = Tx(AccountA, TransactionDirection.Income, isTransfer: false, description: "Balance adjustment", isAdjustment: true);
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [normal, adjustment]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(IsAdjustment: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        TransactionDto only = result.Value.Items.Single();
        only.IsAdjustment.Should().BeTrue();
        only.Description.Should().Be("Balance adjustment");
    }

    [Fact]
    public async Task Handle_MdlTransaction_ProjectsAmountMdlAsIdentity()
    {
        Transaction tx = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, currency: "MDL", amount: 250m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [tx]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(),
            CancellationToken.None);

        TransactionDto dto = result.Value.Items.Single();
        dto.Currency.Should().Be("MDL");
        dto.AmountMdl.Should().Be(250m);
    }

    [Fact]
    public async Task Handle_UsdTransaction_WithDirectRate_ConvertsAmountMdl()
    {
        Transaction tx = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, currency: "USD", amount: 100m);
        FxRate rate = FxRate.Create("USD", "MDL", 17.50m, Today.AddDays(-1)).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [tx],
            fxRates: [rate]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(),
            CancellationToken.None);

        TransactionDto dto = result.Value.Items.Single();
        dto.Currency.Should().Be("USD");
        dto.AmountMdl.Should().Be(1_750m);
    }

    [Fact]
    public async Task Handle_CurrencyWithoutAvailableRate_ProjectsNullAmountMdl()
    {
        Transaction tx = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, currency: "CHF", amount: 100m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [tx]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(),
            CancellationToken.None);

        TransactionDto dto = result.Value.Items.Single();
        dto.Currency.Should().Be("CHF");
        dto.AmountMdl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UsdTransaction_WithOnlyInverseRate_ConvertsViaInverse()
    {
        // Only a MDL->USD rate exists; USD->MDL must fall back to 1/rate.
        Transaction tx = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, currency: "USD", amount: 100m);
        FxRate inverseRate = FxRate.Create("MDL", "USD", 0.05m, Today.AddDays(-1)).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [tx],
            fxRates: [inverseRate]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(),
            CancellationToken.None);

        TransactionDto dto = result.Value.Items.Single();
        // 100 USD * (1 / 0.05) = 2000 MDL.
        dto.AmountMdl.Should().Be(2_000m);
    }

    [Fact]
    public async Task Handle_WithCategoryIdFilter_ReturnsOnlyMatchingRows()
    {
        var categoryId = Guid.CreateVersion7();
        Transaction matching = Transaction.Create(
            AccountA, Today, TransactionDirection.Expense, new Money(40m, "MDL"),
            "groceries", TransactionSource.Manual, categoryId: categoryId).Value;
        Transaction other = Tx(AccountA, TransactionDirection.Expense, isTransfer: false, description: "uncategorized");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [matching, other]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(CategoryId: categoryId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        TransactionDto only = result.Value.Items.Single();
        only.CategoryId.Should().Be(categoryId);
        only.Description.Should().Be("groceries");
    }
}
