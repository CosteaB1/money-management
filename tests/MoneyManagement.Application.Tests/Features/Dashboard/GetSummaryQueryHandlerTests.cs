using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Dashboard.GetSummary;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Dashboard;

public class GetSummaryQueryHandlerTests
{
    private static readonly Guid AccountA = Guid.CreateVersion7();
    private static readonly DateOnly InsideMay = new(2026, 5, 15);
    private static readonly DateOnly MayMonth = new(2026, 5, 1);

    private static Transaction Tx(
        TransactionDirection direction,
        decimal amount,
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
            isTransfer: isTransfer,
            counterAccountId: counterAccountId,
            isAdjustment: isAdjustment);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>
    /// Identity converter — returns the input amount unchanged when from==to,
    /// otherwise returns null. Mirrors <see cref="IFxConverter"/>'s contract.
    /// </summary>
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

    /// <summary>Converter that resolves a fixed table; null for anything else.</summary>
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
    public async Task Handle_FiltersOutIsTransferRows()
    {
        Transaction normal = Tx(TransactionDirection.Income, 1_000m);
        Transaction transferLeg = Tx(
            TransactionDirection.Income, 5_000m,
            isTransfer: true, counterAccountId: Guid.CreateVersion7());

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [normal, transferLeg]);
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Income.Should().Be(1_000m);
        result.Value.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FiltersOutIsAdjustmentRows()
    {
        Transaction normal = Tx(TransactionDirection.Income, 800m);
        Transaction adjustment = Tx(TransactionDirection.Income, 2_000m, isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [normal, adjustment]);
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Income.Should().Be(800m);
        result.Value.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FiltersOutSoftDeletedRows()
    {
        Transaction kept = Tx(TransactionDirection.Expense, 100m);
        Transaction deleted = Tx(TransactionDirection.Expense, 900m);
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [kept, deleted]);
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Expense.Should().Be(100m);
        result.Value.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MultiCurrencyIncome_SumsFxConvertedMdl()
    {
        // 200 USD * 17 = 3400 MDL, 100 EUR * 19 = 1900 MDL, 500 MDL identity.
        // Total expected income = 3400 + 1900 + 500 = 5800.
        Transaction usd = Tx(TransactionDirection.Income, 200m, currency: "USD");
        Transaction eur = Tx(TransactionDirection.Income, 100m, currency: "EUR");
        Transaction mdl = Tx(TransactionDirection.Income, 500m, currency: "MDL");

        IFxConverter fx = TableConverter(new Dictionary<string, decimal>
        {
            ["USD"] = 17m,
            ["EUR"] = 19m,
        });

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [usd, eur, mdl]);
        var handler = new GetSummaryQueryHandler(db, fx, Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Income.Should().Be(5_800m);
        result.Value.MissingFxRate.Should().BeFalse();
        result.Value.TransactionCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_RowWithNoConvertibleRate_SetsMissingFxRateAndOmitsIt()
    {
        Transaction usd = Tx(TransactionDirection.Income, 100m, currency: "USD");
        Transaction chf = Tx(TransactionDirection.Income, 50m, currency: "CHF");

        IFxConverter fx = TableConverter(new Dictionary<string, decimal>
        {
            ["USD"] = 17m,
        });

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [usd, chf]);
        var handler = new GetSummaryQueryHandler(db, fx, Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Income.Should().Be(1_700m); // CHF row omitted
        result.Value.MissingFxRate.Should().BeTrue();
        result.Value.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_EmptyWindow_ReturnsZerosAndZeroSavingsRate()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Income.Should().Be(0m);
        result.Value.Expense.Should().Be(0m);
        result.Value.Net.Should().Be(0m);
        result.Value.SavingsRate.Should().Be(0m);
        result.Value.TransactionCount.Should().Be(0);
        result.Value.MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MonthBoundary_ExcludesLastDayOfPriorMonthAndFirstDayOfNextMonth()
    {
        // Window is March 2026: [March 1, April 1).
        // (March chosen so all boundary dates are in the past relative to
        // wall-clock today — the Transaction factory rejects future dates.)
        Transaction priorMonthLastDay = Tx(
            TransactionDirection.Income, 100m, date: new DateOnly(2026, 2, 28));
        Transaction firstDayInside = Tx(
            TransactionDirection.Income, 200m, date: new DateOnly(2026, 3, 1));
        Transaction lastDayInside = Tx(
            TransactionDirection.Income, 400m, date: new DateOnly(2026, 3, 31));
        Transaction nextMonthFirstDay = Tx(
            TransactionDirection.Income, 800m, date: new DateOnly(2026, 4, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [priorMonthLastDay, firstDayInside, lastDayInside, nextMonthFirstDay]);

        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(new DateOnly(2026, 3, 1)), CancellationToken.None);

        // Only March 1 and March 31 should count.
        result.Value.Income.Should().Be(600m);
        result.Value.TransactionCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DefaultMonthIsNow_ViaInjectedClock()
    {
        // The stub clock anchors the default window. Dates picked here must
        // be in the past relative to real wall-clock today because the
        // Transaction factory rejects future dates with DateInFuture.
        var clockNow = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc);
        // Row inside March.
        Transaction march = Tx(TransactionDirection.Income, 333m, date: new DateOnly(2026, 3, 5));
        // Row inside February — must not count when the default window is "now".
        Transaction february = Tx(TransactionDirection.Income, 999m, date: new DateOnly(2026, 2, 5));

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [march, february]);
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(clockNow));

        // Note: no Month specified — handler must default to "now".
        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(), CancellationToken.None);

        result.Value.Month.Should().Be("2026-03");
        result.Value.Income.Should().Be(333m);
        result.Value.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SavingsRate_IncomeOverExpense_ComputesPositiveRate()
    {
        // 1000 income, 800 expense -> net 200, savingsRate 0.20.
        Transaction income = Tx(TransactionDirection.Income, 1_000m);
        Transaction expense = Tx(TransactionDirection.Expense, 800m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [income, expense]);
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Income.Should().Be(1_000m);
        result.Value.Expense.Should().Be(800m);
        result.Value.Net.Should().Be(200m);
        result.Value.SavingsRate.Should().Be(0.20m);
    }

    [Fact]
    public async Task Handle_SavingsRate_ZeroIncome_ReturnsZeroRate()
    {
        Transaction expense = Tx(TransactionDirection.Expense, 500m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [expense]);
        var handler = new GetSummaryQueryHandler(db, IdentityConverter(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(MayMonth), CancellationToken.None);

        result.Value.Income.Should().Be(0m);
        result.Value.Expense.Should().Be(500m);
        result.Value.Net.Should().Be(-500m);
        result.Value.SavingsRate.Should().Be(0m);
    }
}
