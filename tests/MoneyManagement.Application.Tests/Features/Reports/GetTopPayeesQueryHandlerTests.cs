using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Reports.GetTopPayees;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Reports;

public class GetTopPayeesQueryHandlerTests
{
    private static readonly Guid AccountA = Guid.CreateVersion7();
    private static readonly DateOnly InsideMay = new(2026, 5, 15);
    private static readonly DateOnly FromMay = new(2026, 5, 1);
    private static readonly DateOnly ToMay = new(2026, 5, 31);

    private static Transaction Tx(
        TransactionDirection direction,
        decimal amount,
        string description,
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
            description,
            TransactionSource.Manual,
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
    public async Task Handle_NormalizesDescription_TrimAndLowerCase()
    {
        // Three rows: "LINELLA " trimmed at write time -> "LINELLA",
        // "linella" verbatim, "ANDY'S PIZZA". The first two should collapse
        // into one bucket; "ANDY'S PIZZA" is its own bucket. (Transaction.Create
        // already trims, so what we store is "LINELLA"/"linella"/"ANDY'S PIZZA";
        // case is the only differentiator left for the normalizer to handle.)
        Transaction r1 = Tx(TransactionDirection.Expense, 100m, "LINELLA", date: new DateOnly(2026, 5, 5));
        Transaction r2 = Tx(TransactionDirection.Expense, 50m, "linella", date: new DateOnly(2026, 5, 6));
        Transaction r3 = Tx(TransactionDirection.Expense, 200m, "ANDY'S PIZZA", date: new DateOnly(2026, 5, 7));

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [r1, r2, r3]);
        var handler = new GetTopPayeesQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        TopPayeeDto linella = result.Value.Single(p => p.Payee == "linella");
        linella.AmountMdl.Should().Be(150m);
        linella.TransactionCount.Should().Be(2);
        // OriginalDescription is the earliest occurrence's raw text — "LINELLA".
        linella.OriginalDescription.Should().Be("LINELLA");

        result.Value[0].AmountMdl.Should().BeGreaterThan(result.Value[1].AmountMdl);
    }

    [Fact]
    public async Task Handle_FiltersOutTransfers_Adjustments_AndSoftDeleted()
    {
        Transaction kept = Tx(TransactionDirection.Expense, 100m, "kept");
        Transaction transfer = Tx(
            TransactionDirection.Expense, 999m, "transfer leg",
            isTransfer: true, counterAccountId: Guid.CreateVersion7());
        Transaction adjustment = Tx(
            TransactionDirection.Expense, 888m, "balance adjust",
            isAdjustment: true);
        Transaction deleted = Tx(TransactionDirection.Expense, 777m, "deleted");
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [kept, transfer, adjustment, deleted]);

        var handler = new GetTopPayeesQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Payee.Should().Be("kept");
    }

    [Fact]
    public async Task Handle_LimitClamped_TopHighestAmounts()
    {
        Transaction big = Tx(TransactionDirection.Expense, 500m, "big");
        Transaction medium = Tx(TransactionDirection.Expense, 200m, "medium");
        Transaction small = Tx(TransactionDirection.Expense, 50m, "small");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [big, medium, small]);
        var handler = new GetTopPayeesQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(FromMay, ToMay, TransactionDirection.Expense, Limit: 2),
            CancellationToken.None);

        result.Value.Should().HaveCount(2);
        result.Value[0].Payee.Should().Be("big");
        result.Value[1].Payee.Should().Be("medium");
    }

    [Fact]
    public async Task Handle_LimitBelowMin_ClampsToOne()
    {
        Transaction a = Tx(TransactionDirection.Expense, 10m, "a");
        Transaction b = Tx(TransactionDirection.Expense, 5m, "b");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [a, b]);
        var handler = new GetTopPayeesQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(FromMay, ToMay, TransactionDirection.Expense, Limit: 0),
            CancellationToken.None);

        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_MissingFxRate_OmitsRowFromTotals()
    {
        Transaction mdl = Tx(TransactionDirection.Expense, 100m, "mdl row");
        Transaction usd = Tx(TransactionDirection.Expense, 50m, "usd row", currency: "USD");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [mdl, usd]);
        var handler = new GetTopPayeesQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(FromMay, ToMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Payee.Should().Be("mdl row");
    }

    [Fact]
    public async Task Handle_FromAfterTo_ReturnsRangeOutOfBounds()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetTopPayeesQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(ToMay, FromMay, TransactionDirection.Expense),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reports.range_out_of_bounds");
    }
}
