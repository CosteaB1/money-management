using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Dashboard.GetNetWorth;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Dashboard;

/// <summary>
/// The live net-worth card. Before this slice the dashboard counted borrowed
/// money as wealth: a received loan raised an account balance and nothing
/// subtracted the obligation. Every qualification rule that decides whether a
/// loan is netted out is pinned here.
/// </summary>
public sealed class GetNetWorthQueryHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly LoanDate = new(2026, 1, 15);
    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Account NewAccount(decimal balance, string currency = "MDL", bool archived = false)
    {
        Result<Account> result = Account.Create(
            $"Acct {Guid.NewGuid():N}",
            AccountType.Cash,
            new Money(balance, currency),
            LongAgo,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        if (archived)
        {
            result.Value.Archive();
        }

        return result.Value;
    }

    private static Transaction Tx(Guid accountId, TransactionDirection direction, decimal amount)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            new DateOnly(2026, 2, 1),
            direction,
            new Money(amount, "MDL"),
            "row",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Loan NewLoan(
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
            LoanDate,
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

    private static LoanPayment NewPayment(
        Guid loanId,
        decimal amount,
        string currency = "MDL",
        DateOnly? occurredOn = null)
    {
        Result<LoanPayment> result = LoanPayment.Create(
            loanId,
            new Money(amount, currency),
            currency,
            LoanDate,
            occurredOn ?? new DateOnly(2026, 3, 1),
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>
    /// Wired to the REAL claim source so the loan qualification rules are
    /// exercised together with the arithmetic, the way the endpoint runs them.
    /// </summary>
    private static GetNetWorthQueryHandler Handler(IApplicationDbContext db, IFxConverter? fx = null) =>
        new(db, fx ?? FakeFxConverter.Identity(), new LoanExternalClaimSource(db), [], Clock());

    [Fact]
    public async Task Handle_NoAccountsNoLoans_ReturnsAllZeroes()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new NetWorthDto(0m, 0m, 0m, 0m, 0, 0, 0m));
    }

    [Fact]
    public async Task Handle_NetWorth_IsGrossMinusLiabilitiesPlusExternalAssets()
    {
        Account account = NewAccount(10_000m);
        Loan borrowed = NewLoan(LoanDirection.Received, 2_000m);
        Loan lent = NewLoan(LoanDirection.Given, 500m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [borrowed, lent]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        NetWorthDto dto = result.Value;
        dto.GrossAssetsMdl.Should().Be(10_000m);
        dto.ExternalLiabilitiesMdl.Should().Be(2_000m, "liabilities are reported as a positive magnitude");
        dto.ExternalAssetsMdl.Should().Be(500m, "receivables are reported as a positive magnitude");
        dto.NetWorthMdl.Should().Be(8_500m);
        dto.NetWorthMdl.Should().Be(dto.GrossAssetsMdl - dto.ExternalLiabilitiesMdl + dto.ExternalAssetsMdl);
    }

    [Fact]
    public async Task Handle_ReceivedLoan_DecreasesNetWorth()
    {
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [NewLoan(LoanDirection.Received, 2_000m)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.NetWorthMdl.Should().Be(8_000m);
    }

    [Fact]
    public async Task Handle_GivenLoan_IncreasesNetWorth()
    {
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [NewLoan(LoanDirection.Given, 2_000m)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.NetWorthMdl.Should().Be(12_000m);
    }

    [Fact]
    public async Task Handle_UnlinkedLoan_IsExcluded()
    {
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [NewLoan(accountLinked: false, principal: 2_000m)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.ExternalLiabilitiesMdl.Should().Be(0m);
        result.Value.NetWorthMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_ArchivedButUnsettledLoan_IsStillDeducted()
    {
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [NewLoan(archived: true, principal: 2_000m)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.ExternalLiabilitiesMdl.Should().Be(2_000m);
        result.Value.NetWorthMdl.Should().Be(8_000m);
    }

    [Fact]
    public async Task Handle_SettledLoan_ContributesNothing()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(principal: 2_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [loan],
            loanPayments: [NewPayment(loan.Id, 2_000m)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.ExternalLiabilitiesMdl.Should().Be(0m);
        result.Value.NetWorthMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_PartiallyRepaidLoan_DeductsOnlyTheRemainder()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(principal: 2_000m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [loan],
            loanPayments: [NewPayment(loan.Id, 750m)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.ExternalLiabilitiesMdl.Should().Be(1_250m);
        result.Value.NetWorthMdl.Should().Be(8_750m);
    }

    [Fact]
    public async Task Handle_ClaimWithNoUsableRate_IsOmittedAndCounted()
    {
        Account account = NewAccount(10_000m);

        // The identity converter has no EUR to MDL rate, so the claim cannot be
        // valued and must drop out rather than fall back to 1:1.
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [NewLoan(principal: 6_000m, currency: "EUR")]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.LoansMissingFxRate.Should().Be(1);
        result.Value.ExternalLiabilitiesMdl.Should().Be(0m);
        result.Value.NetWorthMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_SettledClaimWithNoUsableRate_DoesNotTripTheCounter()
    {
        Account account = NewAccount(10_000m);
        Loan loan = NewLoan(principal: 1_000m, currency: "EUR");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [loan],
            loanPayments: [NewPayment(loan.Id, 1_000m, currency: "EUR")]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.LoansMissingFxRate.Should().Be(0, "a settled claim never needs a rate");
        result.Value.NetWorthMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_AccountWithNoUsableRate_IsOmittedAndCounted()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [NewAccount(10_000m), NewAccount(100m, "USD")]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.AccountsMissingFxRate.Should().Be(1);
        result.Value.GrossAssetsMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_ArchivedAccounts_AreExcludedFromGrossAssets()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [NewAccount(10_000m), NewAccount(9_999m, archived: true)]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_GrossAssets_ApplyTransactionsToTheOpeningAnchor()
    {
        Account account = NewAccount(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions:
            [
                Tx(account.Id, TransactionDirection.Income, 500m),
                Tx(account.Id, TransactionDirection.Expense, 200m),
            ]);

        Result<NetWorthDto> result = await Handler(db).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(1_300m);
    }

    [Fact]
    public async Task Handle_ConvertsBothLegsAtTodaysRate()
    {
        Account account = NewAccount(1_000m, "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            loans: [NewLoan(principal: 100m, currency: "EUR")]);

        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal> { ["EUR"] = 20m });

        Result<NetWorthDto> result = await Handler(db, fx).Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.GrossAssetsMdl.Should().Be(20_000m);
        result.Value.ExternalLiabilitiesMdl.Should().Be(2_000m);
        result.Value.NetWorthMdl.Should().Be(18_000m);

        // The card is a "today" view: neither leg may be valued at some other date.
        await fx.Received().ConvertAsync(
            Arg.Any<decimal>(), "EUR", ReportingCurrencies.Mdl, Today, Arg.Any<CancellationToken>());
        await fx.DidNotReceive().ConvertAsync(
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<DateOnly>(d => d != Today),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClaimNotYetEffective_IsIgnored()
    {
        // Loan.Create refuses future dates, so only a stubbed source can produce
        // this shape — the handler still has to hold the line.
        Account account = NewAccount(10_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new GetNetWorthQueryHandler(
            db,
            FakeFxConverter.Identity(),
            FakeExternalClaimSource.With(new ExternalClaim(
                5_000m,
                "MDL",
                Today.AddDays(1),
                ExternalClaimSide.ReducesNetWorth,
                [])),
            [],
            Clock());

        Result<NetWorthDto> result = await handler.Handle(new GetNetWorthQuery(), CancellationToken.None);

        result.Value.ExternalLiabilitiesMdl.Should().Be(0m);
        result.Value.NetWorthMdl.Should().Be(10_000m);
    }
}
