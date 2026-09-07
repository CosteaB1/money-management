using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Dashboard;

/// <summary>
/// The trend line is net OF CLAIMS AS OF each point, not as of today. A loan
/// taken in April must not depress the March point, and a repayment made in May
/// must not shrink the April obligation — otherwise the chart rewrites history
/// every time the user records a payment.
/// </summary>
public sealed class NetWorthTrendClaimDeductionTests
{
    // Points for months = 3 are: Mar 31, Apr 30, May 20 (today).
    private static readonly DateTime ClockNow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(ClockNow);
        return clock;
    }

    private static Account NewAccount(decimal opening, string currency = "MDL")
    {
        Result<Account> result = Account.Create(
            "Cash",
            AccountType.Cash,
            new Money(opening, currency),
            LongAgo,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Loan NewLoan(
        DateOnly loanDate,
        LoanDirection direction = LoanDirection.Received,
        decimal principal = 1_000m,
        string currency = "MDL",
        bool accountLinked = true,
        bool archived = false)
    {
        Result<Loan> result = Loan.Create(
            direction,
            "Ion",
            new Money(principal, currency),
            loanDate,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        Loan loan = result.Value;

        if (accountLinked)
        {
            loan.SetDisbursementTransaction(Guid.CreateVersion7());
        }

        if (archived)
        {
            loan.Archive();
        }

        return loan;
    }

    private static LoanPayment NewPayment(Loan loan, decimal amount, DateOnly occurredOn)
    {
        Result<LoanPayment> result = LoanPayment.Create(
            loan.Id,
            new Money(amount, loan.Principal.Currency),
            loan.Principal.Currency,
            loan.LoanDate,
            occurredOn,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static GetNetWorthTrendQueryHandler Handler(IApplicationDbContext db, IFxConverter? fx = null) =>
        new(db, fx ?? FakeFxConverter.Identity(), new LoanExternalClaimSource(db), [], Clock());

    [Fact]
    public async Task Trend_ReceivedLoan_DoesNotDepressPointsBeforeItExisted()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 4, 10), principal: 3_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value.Should().HaveCount(3);
        result.Value[0].Month.Should().Be("2026-03");
        result.Value[0].NetWorthMdl.Should().Be(10_000m, "the loan did not exist on Mar 31");
        result.Value[1].NetWorthMdl.Should().Be(7_000m, "Apr 30 is after the Apr 10 loan date");
        result.Value[2].NetWorthMdl.Should().Be(7_000m);
    }

    [Fact]
    public async Task Trend_PaymentAfterAPoint_DoesNotShrinkThatPointsObligation()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 1, 5), principal: 3_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [loan],
            loanPayments: [NewPayment(loan, 1_000m, new DateOnly(2026, 5, 2))]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        // Mar 31 and Apr 30 predate the payment, so the full 3,000 is still owed.
        result.Value[0].NetWorthMdl.Should().Be(7_000m);
        result.Value[1].NetWorthMdl.Should().Be(7_000m);
        // May 20: 1,000 repaid, 2,000 still owed.
        result.Value[2].NetWorthMdl.Should().Be(8_000m);
    }

    [Fact]
    public async Task Trend_GivenLoan_LiftsEveryPointAfterItsLoanDate()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 4, 10), LoanDirection.Given, principal: 3_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(10_000m);
        result.Value[1].NetWorthMdl.Should().Be(13_000m);
        result.Value[2].NetWorthMdl.Should().Be(13_000m);
    }

    [Fact]
    public async Task Trend_UnlinkedLoan_NeverAffectsAnyPoint()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 1, 5), principal: 3_000m, accountLinked: false);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value.Should().OnlyContain(p => p.NetWorthMdl == 10_000m);
    }

    [Fact]
    public async Task Trend_ArchivedButUnsettledLoan_StillDepressesEveryPoint()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 1, 5), principal: 3_000m, archived: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value.Should().OnlyContain(p => p.NetWorthMdl == 7_000m);
    }

    [Fact]
    public async Task Trend_LoanSettledMidSeries_StopsDepressingLaterPoints()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 1, 5), principal: 3_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [loan],
            loanPayments: [NewPayment(loan, 3_000m, new DateOnly(2026, 4, 15))]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(7_000m, "still owed on Mar 31");
        result.Value[1].NetWorthMdl.Should().Be(10_000m, "settled Apr 15");
        result.Value[2].NetWorthMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Trend_ClaimWithNoRateAtAPoint_TripsMissingFxRateForThatPointOnly()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(new DateOnly(2026, 1, 5), principal: 100m, currency: "EUR");

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);

        // EUR resolves only from April onwards — the claim leg must be valued at
        // the POINT's date, so March has no rate and drops out.
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

                if (from == "EUR" && to == "MDL" && asOf >= new DateOnly(2026, 4, 1))
                {
                    return Task.FromResult<decimal?>(amount * 20m);
                }

                return Task.FromResult<decimal?>(null);
            });

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db, fx).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        result.Value[0].NetWorthMdl.Should().Be(10_000m, "the unconvertible claim is omitted, not guessed at 1:1");
        result.Value[0].MissingFxRate.Should().BeTrue();
        result.Value[1].NetWorthMdl.Should().Be(8_000m);
        result.Value[1].MissingFxRate.Should().BeFalse();
        result.Value[2].NetWorthMdl.Should().Be(8_000m);
        result.Value[2].MissingFxRate.Should().BeFalse();
    }
}
