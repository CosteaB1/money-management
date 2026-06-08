using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Dashboard;
using MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Dashboard;

public class GetNetWorthTrendQueryHandlerTests
{
    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    private static Account NewAccount(string name, string currency, decimal opening, DateOnly? openingDate = null)
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(opening, currency),
            openingDate ?? LongAgo,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction Tx(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        DateOnly date,
        string currency = "MDL")
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date,
            direction,
            new Money(amount, currency),
            "row",
            TransactionSource.Manual);

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

    /// <summary>Date-conditioned converter — USD-&gt;MDL only after the cutoff.</summary>
    private static IFxConverter UsdMdlConverterAfter(DateOnly cutoff, decimal rate)
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
                DateOnly asOf = call.ArgAt<DateOnly>(3);

                if (string.Equals(from, to, StringComparison.Ordinal))
                {
                    return Task.FromResult<decimal?>(amount);
                }

                if (from == "USD" && to == "MDL" && asOf >= cutoff)
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
    public async Task Handle_MonthsOne_ReturnsSinglePointAtToday()
    {
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);
        Account a = NewAccount("Wallet", "MDL", 1_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [a]);
        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(now));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Month.Should().Be("2026-05");
        result.Value[0].NetWorthMdl.Should().Be(1_000m);
        result.Value[0].MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MonthsThree_ReturnsTwoPastMonthEndsPlusToday()
    {
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);
        Account a = NewAccount("Wallet", "MDL", 1_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [a]);
        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(now));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        // Oldest first: March-end, April-end, today (May).
        result.Value[0].Month.Should().Be("2026-03");
        result.Value[1].Month.Should().Be("2026-04");
        result.Value[2].Month.Should().Be("2026-05");
    }

    [Fact]
    public async Task Handle_ArchivedAccounts_AreExcluded()
    {
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);
        Account live = NewAccount("Live", "MDL", 1_000m);
        Account archived = NewAccount("Archived", "MDL", 9_999m);
        archived.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [live, archived]);
        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(now));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(1), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(1_000m);
    }

    [Fact]
    public async Task Handle_PerMonthSum_UsesTransactionsUpToAsOfOnly()
    {
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);
        Account a = NewAccount("Wallet", "MDL", 100m);

        // Income in February 2026 -> visible from Feb-end onwards.
        Transaction feb = Tx(a.Id, TransactionDirection.Income, 200m, new DateOnly(2026, 2, 10));
        // Income in April 2026 -> visible from Apr-end onwards.
        Transaction apr = Tx(a.Id, TransactionDirection.Income, 50m, new DateOnly(2026, 4, 10));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [feb, apr]);

        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(now));

        // months = 4 -> Feb-end, Mar-end, Apr-end, today (May).
        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(4), CancellationToken.None);

        result.Value.Should().HaveCount(4);
        // Feb-end: anchor 100 + Feb tx 200 = 300.
        result.Value[0].Month.Should().Be("2026-02");
        result.Value[0].NetWorthMdl.Should().Be(300m);
        // Mar-end: still 300, no March tx.
        result.Value[1].Month.Should().Be("2026-03");
        result.Value[1].NetWorthMdl.Should().Be(300m);
        // Apr-end: + 50 = 350.
        result.Value[2].Month.Should().Be("2026-04");
        result.Value[2].NetWorthMdl.Should().Be(350m);
        // Today (May): still 350.
        result.Value[3].Month.Should().Be("2026-05");
        result.Value[3].NetWorthMdl.Should().Be(350m);
    }

    [Fact]
    public async Task Handle_AccountWithNoRateAtOnePoint_TripsMissingFxRateForThatPointOnly()
    {
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);

        // USD account, opening 100 USD. Rate available only from April onwards.
        Account usd = NewAccount("USD wallet", "USD", 100m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [usd]);
        var handler = new GetNetWorthTrendQueryHandler(
            db,
            UsdMdlConverterAfter(new DateOnly(2026, 4, 1), 17m),
            Clock(now));

        // months = 3 -> Mar-end (no rate), Apr-end (rate), today (May).
        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value.Should().HaveCount(3);
        // Mar-end: missing rate -> account omitted -> net worth 0, missing true.
        result.Value[0].Month.Should().Be("2026-03");
        result.Value[0].NetWorthMdl.Should().Be(0m);
        result.Value[0].MissingFxRate.Should().BeTrue();
        // Apr-end: rate available -> 100 * 17 = 1700.
        result.Value[1].Month.Should().Be("2026-04");
        result.Value[1].NetWorthMdl.Should().Be(1_700m);
        result.Value[1].MissingFxRate.Should().BeFalse();
        // Today (May): also 1700.
        result.Value[2].NetWorthMdl.Should().Be(1_700m);
        result.Value[2].MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TransactionDatedAfterAsOf_DoesNotContributeToThatPoint()
    {
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);
        Account a = NewAccount("Wallet", "MDL", 0m);

        // Income on May 20 — falls AFTER any prior month-end as-of date.
        Transaction late = Tx(a.Id, TransactionDirection.Income, 555m, new DateOnly(2026, 5, 20));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [late]);

        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(now));

        // months = 2 -> Apr-end + today.
        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(2), CancellationToken.None);

        // Apr-end: opening 0, tx is in May -> excluded.
        result.Value[0].Month.Should().Be("2026-04");
        result.Value[0].NetWorthMdl.Should().Be(0m);
        // Today (May 22) is after May 20 -> tx counts.
        result.Value[1].Month.Should().Be("2026-05");
        result.Value[1].NetWorthMdl.Should().Be(555m);
    }

    [Fact]
    public async Task Handle_MonthsZero_ReturnsFailure()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(DateTime.UtcNow));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(0), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DashboardErrors.MonthsOutOfRange(1, 24));
    }

    [Fact]
    public async Task Handle_MonthsTwentyFive_ReturnsFailure()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetNetWorthTrendQueryHandler(db, IdentityConverter(), Clock(DateTime.UtcNow));

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(25), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DashboardErrors.MonthsOutOfRange(1, 24));
    }
}
