using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Dashboard.GetSummary;
using MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;
using MoneyManagement.Application.Features.Reports.GetMonthlySummary;
using MoneyManagement.Application.Features.Reports.GetTopPayees;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Reports;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): probes the income/expense
/// aggregate handlers' filter discipline (must drop BOTH IsTransfer AND
/// IsAdjustment), missing-FX-row semantics (omitted from totals + flagged, NOT
/// counted), and month-window boundary inclusivity ([first, firstOfNext)).
/// </summary>
public sealed class AggregateEdgeCaseTests
{
    private static readonly Guid AccountA = Guid.CreateVersion7();

    private static IDateTimeProvider Clock(DateTime now)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    private static Transaction Tx(
        TransactionDirection direction,
        decimal amount,
        DateOnly date,
        Guid? categoryId = null,
        string currency = "MDL",
        bool isTransfer = false,
        bool isAdjustment = false,
        string description = "row")
    {
        Result<Transaction> result = Transaction.Create(
            AccountA,
            date,
            direction,
            new Money(amount, currency),
            description,
            TransactionSource.Manual,
            categoryId: categoryId,
            isTransfer: isTransfer,
            counterAccountId: isTransfer ? Guid.CreateVersion7() : null,
            isAdjustment: isAdjustment);

        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static Category Cat(string name, CategoryFlow flow = CategoryFlow.Expense) =>
        Category.Create(name, flow).Value;

    // ------------------------------------------------------------------
    // DASHBOARD SUMMARY — both flags excluded; missing FX is flagged + omitted
    // from totals AND from the count (count tracks converted rows only).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Summary_ExcludesBothTransferAndAdjustment_AndCountsOnlyRealRows()
    {
        var month = new DateOnly(2026, 5, 1);
        Transaction real = Tx(TransactionDirection.Expense, 100m, new DateOnly(2026, 5, 10));
        Transaction transfer = Tx(TransactionDirection.Expense, 5000m, new DateOnly(2026, 5, 11), isTransfer: true);
        Transaction adjustment = Tx(TransactionDirection.Income, 9000m, new DateOnly(2026, 5, 12), isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [real, transfer, adjustment]);
        var handler = new GetSummaryQueryHandler(db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(new GetSummaryQuery(month), CancellationToken.None);

        result.Value.Expense.Should().Be(100m);
        result.Value.Income.Should().Be(0m);
        result.Value.TransactionCount.Should().Be(1);
        result.Value.MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Summary_MissingFxRow_IsFlaggedOmittedFromTotalsAndNotCounted()
    {
        var month = new DateOnly(2026, 5, 1);
        Transaction mdl = Tx(TransactionDirection.Expense, 100m, new DateOnly(2026, 5, 10));
        Transaction usd = Tx(TransactionDirection.Expense, 50m, new DateOnly(2026, 5, 11), currency: "USD");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [mdl, usd]);
        var handler = new GetSummaryQueryHandler(db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(new GetSummaryQuery(month), CancellationToken.None);

        result.Value.Expense.Should().Be(100m);
        result.Value.MissingFxRate.Should().BeTrue();
        // The USD row was unconvertible: not counted.
        result.Value.TransactionCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // MONTH WINDOW boundary: a row dated on the LAST day of the month is IN,
    // a row dated on the FIRST day of the next month is OUT. The window is
    // [firstDay, firstDayOfNextMonth).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Summary_MonthEndBoundary_LastDayIncluded_FirstOfNextExcluded()
    {
        var month = new DateOnly(2026, 5, 1);
        Transaction lastDay = Tx(TransactionDirection.Expense, 31m, new DateOnly(2026, 5, 31));
        Transaction nextFirst = Tx(TransactionDirection.Expense, 1m, new DateOnly(2026, 6, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [lastDay, nextFirst]);
        var handler = new GetSummaryQueryHandler(db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(new GetSummaryQuery(month), CancellationToken.None);

        result.Value.Expense.Should().Be(31m);
        result.Value.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Summary_DecemberToJanuaryRollover_OnlyDecemberCounted()
    {
        var month = new DateOnly(2025, 12, 1);
        Transaction dec31 = Tx(TransactionDirection.Income, 500m, new DateOnly(2025, 12, 31));
        Transaction jan1 = Tx(TransactionDirection.Income, 999m, new DateOnly(2026, 1, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [dec31, jan1]);
        var handler = new GetSummaryQueryHandler(db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)));

        Result<DashboardSummaryDto> result = await handler.Handle(new GetSummaryQuery(month), CancellationToken.None);

        result.Value.Income.Should().Be(500m);
        result.Value.TransactionCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // MONTHLY SUMMARY — a month whose ONLY activity is an unconvertible row is
    // flagged missing AND its count stays 0 (the row never converted).
    // Zero-activity months still appear.
    // ------------------------------------------------------------------

    [Fact]
    public async Task MonthlySummary_MonthWithOnlyMissingFxRow_FlagsMissing_CountZero_AndZeroMonthsStillAppear()
    {
        Transaction usd = Tx(TransactionDirection.Expense, 50m, new DateOnly(2026, 4, 10), currency: "USD");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [usd]);
        var handler = new GetMonthlySummaryQueryHandler(
            db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 1)),
            CancellationToken.None);

        result.Value.Should().HaveCount(3); // Mar, Apr, May — no gaps
        MonthlySummaryPointDto april = result.Value.Single(p => p.Month == "2026-04");
        april.MissingFxRate.Should().BeTrue();
        april.TransactionCount.Should().Be(0);
        april.Expense.Should().Be(0m);

        result.Value.Single(p => p.Month == "2026-03").TransactionCount.Should().Be(0);
        result.Value.Single(p => p.Month == "2026-05").TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task MonthlySummary_ExcludesBothTransferAndAdjustment()
    {
        Transaction real = Tx(TransactionDirection.Expense, 100m, new DateOnly(2026, 5, 10));
        Transaction transfer = Tx(TransactionDirection.Expense, 5000m, new DateOnly(2026, 5, 11), isTransfer: true);
        Transaction adj = Tx(TransactionDirection.Income, 9000m, new DateOnly(2026, 5, 12), isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [real, transfer, adj]);
        var handler = new GetMonthlySummaryQueryHandler(
            db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1)),
            CancellationToken.None);

        MonthlySummaryPointDto may = result.Value.Single();
        may.Expense.Should().Be(100m);
        may.Income.Should().Be(0m);
        may.TransactionCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // CATEGORY BREAKDOWN — "to" date is INCLUSIVE (>= from && <= to).
    // ------------------------------------------------------------------

    [Fact]
    public async Task CategoryBreakdown_ToDateIsInclusive()
    {
        Category food = Cat("Food");
        Transaction onTo = Tx(TransactionDirection.Expense, 100m, new DateOnly(2026, 5, 31), categoryId: food.Id);
        Transaction afterTo = Tx(TransactionDirection.Expense, 999m, new DateOnly(2026, 6, 1), categoryId: food.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [onTo, afterTo], categories: [food]);
        var handler = new GetCategoryBreakdownQueryHandler(db, FakeFxConverter.Identity());

        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), TransactionDirection.Expense),
            CancellationToken.None);

        result.Value.TotalMdl.Should().Be(100m);
    }

    // ------------------------------------------------------------------
    // TOP PAYEES — both flags excluded; normalization collapses casing so the
    // same payee in different case merges into one bucket.
    // ------------------------------------------------------------------

    [Fact]
    public async Task TopPayees_ExcludesBothTransferAndAdjustment_AndMergesCaseInsensitively()
    {
        Transaction a = Tx(TransactionDirection.Expense, 100m, new DateOnly(2026, 5, 10), description: "Linella");
        Transaction b = Tx(TransactionDirection.Expense, 50m, new DateOnly(2026, 5, 11), description: "LINELLA");
        Transaction transfer = Tx(TransactionDirection.Expense, 5000m, new DateOnly(2026, 5, 12), isTransfer: true, description: "Linella");
        Transaction adj = Tx(TransactionDirection.Expense, 9000m, new DateOnly(2026, 5, 13), isAdjustment: true, description: "Linella");

        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [a, b, transfer, adj]);
        var handler = new GetTopPayeesQueryHandler(db, FakeFxConverter.Identity());

        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), TransactionDirection.Expense, 10),
            CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value[0].AmountMdl.Should().Be(150m);
        result.Value[0].TransactionCount.Should().Be(2);
    }
}
