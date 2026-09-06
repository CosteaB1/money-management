using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

/// <summary>
/// Qualification rules for turning loans into net-worth claims. These are the
/// rules that decide whether a debt shows up in net worth at all, so each one
/// gets its own case.
/// </summary>
public sealed class LoanExternalClaimSourceTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LoanDate = new(2026, 1, 15);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
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

    private static LoanPayment NewPayment(Guid loanId, decimal amount, DateOnly occurredOn, string currency = "MDL")
    {
        Result<LoanPayment> result = LoanPayment.Create(
            loanId,
            new Money(amount, currency),
            currency,
            LoanDate,
            occurredOn,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task GetHistory_NoLoans_ReturnsEmpty()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        claims.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistory_UnlinkedLoan_IsExcluded()
    {
        // Never moved a tracked balance, so its cash isn't in gross assets and
        // the obligation must not be netted out either.
        Loan loan = NewLoan(accountLinked: false);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        claims.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistory_ArchivedLoan_IsIncluded()
    {
        // Deliberate divergence from the /loans summary tiles: archiving is
        // bookkeeping-only, so a hidden debt is still a debt.
        Loan loan = NewLoan(archived: true);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        claims.Should().ContainSingle();
        claims[0].OutstandingAsOf(new DateOnly(2026, 5, 1)).Should().Be(1_000m);
    }

    [Fact]
    public async Task GetHistory_ReceivedLoan_ReducesNetWorth()
    {
        Loan loan = NewLoan(LoanDirection.Received);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        claims.Should().ContainSingle();
        claims[0].Side.Should().Be(ExternalClaimSide.ReducesNetWorth);
    }

    [Fact]
    public async Task GetHistory_GivenLoan_IncreasesNetWorth()
    {
        Loan loan = NewLoan(LoanDirection.Given);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        claims.Should().ContainSingle();
        claims[0].Side.Should().Be(ExternalClaimSide.IncreasesNetWorth);
    }

    [Fact]
    public async Task GetHistory_CarriesNativeAmountCurrencyAndLoanDate()
    {
        Loan loan = NewLoan(principal: 6_000m, currency: "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        ExternalClaim claim = claims.Should().ContainSingle().Subject;
        claim.Amount.Should().Be(6_000m);
        claim.Currency.Should().Be("EUR");
        claim.EffectiveFrom.Should().Be(LoanDate);
    }

    [Fact]
    public async Task GetHistory_AttachesEachLoansOwnPayments()
    {
        Loan a = NewLoan(principal: 1_000m);
        Loan b = NewLoan(principal: 2_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [a, b],
            loanPayments:
            [
                NewPayment(a.Id, 250m, new DateOnly(2026, 2, 1)),
                NewPayment(a.Id, 150m, new DateOnly(2026, 3, 1)),
                NewPayment(b.Id, 500m, new DateOnly(2026, 3, 1)),
            ]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        var asOf = new DateOnly(2026, 5, 1);
        claims.Should().HaveCount(2);
        claims.Single(c => c.Amount == 1_000m).OutstandingAsOf(asOf).Should().Be(600m);
        claims.Single(c => c.Amount == 2_000m).OutstandingAsOf(asOf).Should().Be(1_500m);
    }

    [Fact]
    public async Task GetHistory_LoanWithoutPayments_HasNoSettlements()
    {
        Loan loan = NewLoan(principal: 1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);

        IReadOnlyList<ExternalClaim> claims =
            await new LoanExternalClaimSource(db).GetHistoryAsync(CancellationToken.None);

        claims.Should().ContainSingle();
        claims[0].Settlements.Should().BeEmpty();
    }
}
