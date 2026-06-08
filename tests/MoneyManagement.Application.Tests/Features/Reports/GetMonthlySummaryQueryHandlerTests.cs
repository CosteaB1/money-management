using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Reports;
using MoneyManagement.Application.Features.Reports.GetMonthlySummary;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Reports;

public class GetMonthlySummaryQueryHandlerTests
{
    private static readonly Guid AccountA = Guid.CreateVersion7();

    private static Transaction Tx(
        TransactionDirection direction,
        decimal amount,
        DateOnly date,
        string currency = "MDL",
        bool isTransfer = false,
        bool isAdjustment = false,
        Guid? counterAccountId = null)
    {
        Result<Transaction> result = Transaction.Create(
            AccountA,
            date,
            direction,
            new Money(amount, currency),
            "row",
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

    private static IFxConverter TableConverter(Dictionary<string, decimal> ratesToMdl)
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

                if (string.Equals(from, to, StringComparison.Ordinal))
                {
                    return Task.FromResult<decimal?>(amount);
                }

                if (to == "MDL" && ratesToMdl.TryGetValue(from, out decimal rate))
                {
                    return Task.FromResult<decimal?>(amount * rate);
                }

                return Task.FromResult<decimal?>(null);
            });
        return fx;
    }

    private static IDateTimeProvider Clock(DateTime utcNow)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

    [Fact]
    public async Task Handle_ReturnsOneSlotPerMonthInRange_OldestFirst()
    {
        Transaction t1 = Tx(TransactionDirection.Income, 100m, new DateOnly(2026, 2, 10));
        Transaction t2 = Tx(TransactionDirection.Expense, 30m, new DateOnly(2026, 3, 15));

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [t1, t2]);
        var handler = new GetMonthlySummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].Month.Should().Be("2026-02");
        result.Value[0].Income.Should().Be(100m);
        result.Value[1].Month.Should().Be("2026-03");
        result.Value[1].Expense.Should().Be(30m);
        result.Value[2].Month.Should().Be("2026-04");
        result.Value[2].TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_FiltersOutTransfers_Adjustments_AndSoftDeleted()
    {
        Transaction kept = Tx(TransactionDirection.Income, 100m, new DateOnly(2026, 3, 10));
        Transaction transfer = Tx(
            TransactionDirection.Income, 5_000m, new DateOnly(2026, 3, 11),
            isTransfer: true, counterAccountId: Guid.CreateVersion7());
        Transaction adjustment = Tx(
            TransactionDirection.Income, 9_000m, new DateOnly(2026, 3, 12),
            isAdjustment: true);
        Transaction deleted = Tx(TransactionDirection.Income, 7_000m, new DateOnly(2026, 3, 13));
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [kept, transfer, adjustment, deleted]);

        var handler = new GetMonthlySummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Income.Should().Be(100m);
        result.Value[0].TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MissingFxRate_FlipsFlagAndOmitsRow()
    {
        Transaction usd = Tx(TransactionDirection.Income, 100m, new DateOnly(2026, 3, 10), currency: "USD");
        Transaction chf = Tx(TransactionDirection.Income, 50m, new DateOnly(2026, 3, 11), currency: "CHF");

        IFxConverter fx = TableConverter(new Dictionary<string, decimal>
        {
            ["USD"] = 17m,
        });

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [usd, chf]);
        var handler = new GetMonthlySummaryQueryHandler(db, fx, Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        result.Value[0].Income.Should().Be(1_700m);
        result.Value[0].MissingFxRate.Should().BeTrue();
        result.Value[0].TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FromAfterTo_ReturnsRangeOutOfBounds()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetMonthlySummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 5, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reports.range_out_of_bounds");
    }

    [Fact]
    public async Task Handle_SpanGreaterThan24Months_ReturnsRangeOutOfBounds()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetMonthlySummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2024, 1, 1), new DateOnly(2026, 2, 1)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reports.range_out_of_bounds");
    }

    [Fact]
    public async Task Handle_DefaultWindow_ReturnsTrailing12MonthsEndingAtCurrentMonth()
    {
        // Anchor: April 2026 -> default window is May 2025..Apr 2026 (12 points).
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetMonthlySummaryQueryHandler(
            db, IdentityConverter(), Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(), CancellationToken.None);

        result.Value.Should().HaveCount(12);
        result.Value[0].Month.Should().Be("2025-05");
        result.Value[^1].Month.Should().Be("2026-04");
    }

    [Fact]
    public async Task Handle_SavingsRate_IncomeOverExpense_ComputesPerMonth()
    {
        Transaction income = Tx(TransactionDirection.Income, 1_000m, new DateOnly(2026, 3, 5));
        Transaction expense = Tx(TransactionDirection.Expense, 800m, new DateOnly(2026, 3, 7));

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [income, expense]);
        var handler = new GetMonthlySummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        result.Value[0].Income.Should().Be(1_000m);
        result.Value[0].Expense.Should().Be(800m);
        result.Value[0].Net.Should().Be(200m);
        result.Value[0].SavingsRate.Should().Be(0.20m);
    }
}
