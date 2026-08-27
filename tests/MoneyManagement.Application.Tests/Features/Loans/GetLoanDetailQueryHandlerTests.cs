using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Features.Loans.GetLoanDetail;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class GetLoanDetailQueryHandlerTests
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
        string counterparty = "Ion")
    {
        Result<Loan> result = Loan.Create(
            LoanDirection.Given,
            counterparty,
            new Money(principal, currency),
            LoanDate,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static LoanPayment NewPayment(
        Guid loanId,
        decimal amount,
        DateOnly? occurredOn = null,
        DateTime? createdAt = null,
        string currency = "MDL")
    {
        Result<LoanPayment> result = LoanPayment.Create(
            loanId,
            new Money(amount, currency),
            currency,
            LoanDate,
            occurredOn ?? Today,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        LoanPayment payment = result.Value;
        if (createdAt is DateTime created)
        {
            // The audit interceptor stamps CreatedAt in production; unit tests
            // seed it directly through the internal setter (InternalsVisibleTo).
            payment.CreatedAt = created;
        }

        return payment;
    }

    private static Account NewAccount(string name, string currency = "MDL")
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(0m, currency),
            new DateOnly(2026, 1, 1),
            null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction NewTransaction(Guid accountId, decimal amount = 100m, string currency = "MDL")
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            Today,
            TransactionDirection.Income,
            new Money(amount, currency),
            "Loan movement",
            TransactionSource.Manual,
            isTransfer: true);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = result.Value;
        transaction.ClearDomainEvents();
        return transaction;
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        var missingId = Guid.CreateVersion7();
        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_ComputesTotalsAndStatus()
    {
        Loan loan = NewLoan(principal: 1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments:
            [
                NewPayment(loan.Id, 250m, occurredOn: new DateOnly(2026, 2, 1)),
                NewPayment(loan.Id, 150m, occurredOn: new DateOnly(2026, 3, 1)),
            ]);
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(loan.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        LoanDetailDto dto = result.Value;
        dto.TotalRepaid.Should().Be(400m);
        dto.Outstanding.Should().Be(600m);
        dto.OutstandingMdl.Should().Be(600m);
        dto.MissingFxRate.Should().BeFalse();
        dto.Status.Should().Be(LoanStatus.Active);
        dto.PaymentCount.Should().Be(2);
        dto.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OrdersPaymentsByOccurredOnDescThenCreatedAtDesc()
    {
        Loan loan = NewLoan(principal: 1_000m);
        var sameDay = new DateOnly(2026, 3, 1);
        LoanPayment older = NewPayment(
            loan.Id, 100m, occurredOn: sameDay, createdAt: new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        LoanPayment newer = NewPayment(
            loan.Id, 200m, occurredOn: sameDay, createdAt: new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));
        LoanPayment latest = NewPayment(loan.Id, 300m, occurredOn: new DateOnly(2026, 4, 1));
        LoanPayment earliest = NewPayment(loan.Id, 50m, occurredOn: new DateOnly(2026, 2, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [older, newer, latest, earliest]);
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(loan.Id), CancellationToken.None);

        result.Value.Payments.Select(p => p.Id).Should().ContainInOrder(
            latest.Id, newer.Id, older.Id, earliest.Id);
    }

    [Fact]
    public async Task Handle_ResolvesPaymentAndDisbursementAccountsThroughTransactions()
    {
        Account account = NewAccount("Main Wallet");
        Transaction disbursementTx = NewTransaction(account.Id);
        Transaction paymentTx = NewTransaction(account.Id);

        Loan loan = NewLoan();
        loan.SetDisbursementTransaction(disbursementTx.Id);

        LoanPayment payment = NewPayment(loan.Id, 100m);
        payment.LinkTransaction(paymentTx.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [disbursementTx, paymentTx],
            loans: [loan],
            loanPayments: [payment]);
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(loan.Id), CancellationToken.None);

        LoanDetailDto dto = result.Value;
        dto.DisbursementTransactionId.Should().Be(disbursementTx.Id);
        dto.DisbursementAccountId.Should().Be(account.Id);
        dto.DisbursementAccountName.Should().Be("Main Wallet");

        LoanPaymentDto paymentDto = dto.Payments.Single();
        paymentDto.TransactionId.Should().Be(paymentTx.Id);
        paymentDto.AccountId.Should().Be(account.Id);
        paymentDto.AccountName.Should().Be("Main Wallet");
    }

    [Fact]
    public async Task Handle_DanglingTransactionLink_LeavesAccountFieldsNull()
    {
        Loan loan = NewLoan();
        loan.SetDisbursementTransaction(Guid.CreateVersion7());

        LoanPayment payment = NewPayment(loan.Id, 100m);
        payment.LinkTransaction(Guid.CreateVersion7());

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [payment]);
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(loan.Id), CancellationToken.None);

        LoanDetailDto dto = result.Value;
        dto.DisbursementAccountId.Should().BeNull();
        dto.DisbursementAccountName.Should().BeNull();

        LoanPaymentDto paymentDto = dto.Payments.Single();
        paymentDto.TransactionId.Should().NotBeNull();
        paymentDto.AccountId.Should().BeNull();
        paymentDto.AccountName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ArchivedLoan_IsStillReachable()
    {
        Loan loan = NewLoan();
        loan.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(loan.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoUsableRate_FlagsMissingFxRate()
    {
        Loan loan = NewLoan(currency: "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new GetLoanDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<LoanDetailDto> result = await handler.Handle(
            new GetLoanDetailQuery(loan.Id), CancellationToken.None);

        result.Value.OutstandingMdl.Should().BeNull();
        result.Value.MissingFxRate.Should().BeTrue();
    }
}
