using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Features.Loans.GetLoans;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class GetLoansQueryHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly LoanDate = new(2026, 1, 15);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Loan NewLoan(
        decimal principal = 1_000m,
        string currency = "MDL",
        LoanDirection direction = LoanDirection.Given,
        string counterparty = "Ion")
    {
        Result<Loan> result = Loan.Create(
            direction,
            counterparty,
            new Money(principal, currency),
            LoanDate,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static LoanPayment NewPayment(Guid loanId, decimal amount, string currency = "MDL")
    {
        Result<LoanPayment> result = LoanPayment.Create(
            loanId,
            new Money(amount, currency),
            currency,
            LoanDate,
            Today,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_NoLoans_ReturnsEmptyList()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ComputesPerLoanTotalsFromPayments()
    {
        Loan loanA = NewLoan(principal: 1_000m);
        Loan loanB = NewLoan(principal: 5_000m, counterparty: "Maria");
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loanA, loanB],
            loanPayments:
            [
                NewPayment(loanA.Id, 250m),
                NewPayment(loanA.Id, 150m),
                NewPayment(loanB.Id, 1_000m),
            ]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        LoanDto dtoA = result.Value.Single(l => l.Id == loanA.Id);
        dtoA.TotalRepaid.Should().Be(400m);
        dtoA.Outstanding.Should().Be(600m);
        dtoA.PaymentCount.Should().Be(2);
        dtoA.Status.Should().Be(LoanStatus.Active);

        LoanDto dtoB = result.Value.Single(l => l.Id == loanB.Id);
        dtoB.TotalRepaid.Should().Be(1_000m);
        dtoB.Outstanding.Should().Be(4_000m);
        dtoB.PaymentCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_LoanWithNoPayments_HasZeroRepaidAndFullOutstanding()
    {
        Loan loan = NewLoan(principal: 2_500m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        LoanDto dto = result.Value.Single();
        dto.TotalRepaid.Should().Be(0m);
        dto.Outstanding.Should().Be(2_500m);
        dto.PaymentCount.Should().Be(0);
        dto.Status.Should().Be(LoanStatus.Active);
    }

    [Fact]
    public async Task Handle_FullyRepaidLoan_IsSettled()
    {
        Loan loan = NewLoan(principal: 1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [NewPayment(loan.Id, 600m), NewPayment(loan.Id, 400m)]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        LoanDto dto = result.Value.Single();
        dto.Outstanding.Should().Be(0m);
        dto.Status.Should().Be(LoanStatus.Settled);
    }

    [Fact]
    public async Task Handle_MdlLoan_ConvertsOutstandingByIdentity()
    {
        Loan loan = NewLoan(principal: 1_000m, currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [NewPayment(loan.Id, 300m)]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        LoanDto dto = result.Value.Single();
        dto.OutstandingMdl.Should().Be(700m);
        dto.MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ForeignLoanWithRate_ConvertsOutstandingAtToday()
    {
        Loan loan = NewLoan(principal: 1_000m, currency: "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal> { ["EUR"] = 19.5m });
        var handler = new GetLoansQueryHandler(db, fx, Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        LoanDto dto = result.Value.Single();
        dto.OutstandingMdl.Should().Be(1_000m * 19.5m);
        dto.MissingFxRate.Should().BeFalse();
        await fx.Received(1).ConvertAsync(
            1_000m, "EUR", "MDL", Today, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUsableRate_FlagsMissingFxRate()
    {
        Loan loan = NewLoan(currency: "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        // Identity converter yields null for cross-currency conversions.
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        LoanDto dto = result.Value.Single();
        dto.OutstandingMdl.Should().BeNull();
        dto.MissingFxRate.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExcludesArchivedLoans()
    {
        Loan active = NewLoan();
        Loan archived = NewLoan(counterparty: "Maria");
        archived.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [active, archived]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(), CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value.Single().Id.Should().Be(active.Id);
        result.Value.Single().IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_IncludeArchived_ReturnsArchivedRowsWithFlagPopulated()
    {
        Loan active = NewLoan();
        Loan archived = NewLoan(counterparty: "Maria");
        archived.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [active, archived]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(
            new GetLoansQuery(IncludeArchived: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Single(l => l.Id == active.Id).IsArchived.Should().BeFalse();
        result.Value.Single(l => l.Id == archived.Id).IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_IncludeArchived_ComputesTotalsForArchivedRows()
    {
        Loan archived = NewLoan(principal: 1_000m);
        archived.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [archived],
            loanPayments: [NewPayment(archived.Id, 250m)]);
        var handler = new GetLoansQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(
            new GetLoansQuery(IncludeArchived: true), CancellationToken.None);

        LoanDto dto = result.Value.Single();
        dto.IsArchived.Should().BeTrue();
        dto.TotalRepaid.Should().Be(250m);
        dto.Outstanding.Should().Be(750m);
        dto.OutstandingMdl.Should().Be(750m);
        dto.PaymentCount.Should().Be(1);
        dto.Status.Should().Be(LoanStatus.Active);
    }
}
