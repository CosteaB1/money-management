using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Reports;

public class GetCategoryBreakdownQueryHandlerTests
{
    private static readonly Guid AccountA = Guid.CreateVersion7();
    private static readonly DateOnly InsideMay = new(2026, 5, 15);
    private static readonly DateOnly FromMay = new(2026, 5, 1);
    private static readonly DateOnly ToMay = new(2026, 5, 31);

    private static Category Cat(string name, CategoryFlow flow = CategoryFlow.Expense)
    {
        Result<Category> result = Category.Create(name, flow);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction Tx(
        TransactionDirection direction,
        decimal amount,
        Guid? categoryId = null,
        string currency = "MDL",
        DateOnly? date = null,
        bool isTransfer = false,
        bool isAdjustment = false,
        Guid? counterAccountId = null)
    {
        Result<Transaction> result = Transaction.Create(
            AccountA,
            date ?? InsideMay,
            direction,
            new Money(amount, currency),
            "row",
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
    public async Task Handle_HappyPath_GroupsByCategory_SortsByAmountDesc_PercentagesSumToOne()
    {
        Category food = Cat("Food");
        Category rent = Cat("Rent");

        Transaction t1 = Tx(TransactionDirection.Expense, 200m, categoryId: food.Id);
        Transaction t2 = Tx(TransactionDirection.Expense, 100m, categoryId: food.Id);
        Transaction t3 = Tx(TransactionDirection.Expense, 800m, categoryId: rent.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [t1, t2, t3],
            categories: [food, rent]);

        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMdl.Should().Be(1_100m);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].CategoryName.Should().Be("Rent");
        result.Value.Items[0].AmountMdl.Should().Be(800m);
        result.Value.Items[1].CategoryName.Should().Be("Food");
        result.Value.Items[1].AmountMdl.Should().Be(300m);
        result.Value.Items[1].TransactionCount.Should().Be(2);

        // Percentages should sum to 1.0.
        result.Value.Items.Sum(i => i.Percentage).Should().Be(1.0m);
    }

    [Fact]
    public async Task Handle_TransfersAdjustmentsAndSoftDeleted_AreExcluded()
    {
        Category food = Cat("Food");

        Transaction kept = Tx(TransactionDirection.Expense, 100m, categoryId: food.Id);
        Transaction transfer = Tx(
            TransactionDirection.Expense, 5_000m, categoryId: food.Id,
            isTransfer: true, counterAccountId: Guid.CreateVersion7());
        Transaction adjustment = Tx(
            TransactionDirection.Expense, 9_000m, categoryId: food.Id,
            isAdjustment: true);
        Transaction deleted = Tx(TransactionDirection.Expense, 7_000m, categoryId: food.Id);
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [kept, transfer, adjustment, deleted],
            categories: [food]);

        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.TotalMdl.Should().Be(100m);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_UncategorizedBucket_AggregatesNullCategoryRows()
    {
        Category food = Cat("Food");

        Transaction categorized = Tx(TransactionDirection.Expense, 100m, categoryId: food.Id);
        Transaction uncategorized1 = Tx(TransactionDirection.Expense, 60m, categoryId: null);
        Transaction uncategorized2 = Tx(TransactionDirection.Expense, 40m, categoryId: null);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [categorized, uncategorized1, uncategorized2],
            categories: [food]);

        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.Items.Should().HaveCount(2);
        CategoryBreakdownItemDto uncatBucket = result.Value.Items.Single(i => i.CategoryId is null);
        uncatBucket.CategoryName.Should().Be("Uncategorized");
        uncatBucket.AmountMdl.Should().Be(100m);
        uncatBucket.TransactionCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DirectionFilter_ExcludesOppositeDirection()
    {
        Category food = Cat("Food");

        Transaction expense = Tx(TransactionDirection.Expense, 100m, categoryId: food.Id);
        Transaction income = Tx(TransactionDirection.Income, 999m, categoryId: food.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [expense, income],
            categories: [food]);

        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.TotalMdl.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_EmptyResult_PercentagesAreZeroAndItemsEmpty()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.TotalMdl.Should().Be(0m);
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingFxRate_FlipsFlagAndOmitsRow()
    {
        Category food = Cat("Food");

        Transaction mdl = Tx(TransactionDirection.Expense, 100m, categoryId: food.Id);
        Transaction usd = Tx(TransactionDirection.Expense, 50m, categoryId: food.Id, currency: "USD");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [mdl, usd],
            categories: [food]);

        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.MissingFxRate.Should().BeTrue();
        result.Value.TotalMdl.Should().Be(100m);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FromAfterTo_ReturnsRangeOutOfBounds()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetCategoryBreakdownQueryHandler(db, IdentityConverter());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(ToMay, FromMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reports.range_out_of_bounds");
    }
}
