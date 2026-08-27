using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Loans.RecordLoanPayment;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class RecordLoanPaymentCommandHandlerTests
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
        LoanDirection direction = LoanDirection.Received,
        string counterparty = "Parents",
        decimal principal = 1_000m,
        string currency = "EUR")
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

    private static LoanPayment NewPayment(Guid loanId, decimal amount, string currency = "EUR")
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

    private static Account NewAccount(string currency = "EUR")
    {
        Result<Account> result = Account.Create(
            "EUR Wallet",
            AccountType.Cash,
            new Money(0m, currency),
            new DateOnly(2026, 1, 1),
            null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static RecordLoanPaymentCommand NewCommand(
        Guid loanId,
        decimal amount = 400m,
        Guid? accountId = null,
        string? notes = null) =>
        new(loanId, amount, Today, accountId, notes);

    [Fact]
    public async Task Handle_WithoutAccount_PersistsPaymentOnly()
    {
        Loan loan = NewLoan();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id, notes: "first chunk"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        LoanPayment persisted = db.LoanPayments.Single();
        persisted.LoanId.Should().Be(loan.Id);
        persisted.Amount.Amount.Should().Be(400m);
        persisted.Amount.Currency.Should().Be("EUR");
        persisted.OccurredOn.Should().Be(Today);
        persisted.TransactionId.Should().BeNull();
        persisted.Notes.Should().Be("first chunk");
        result.Value.Id.Should().Be(persisted.Id);
        db.Transactions.Should().BeEmpty();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReceivedLoanWithAccount_SynthesizesExpenseRepayment()
    {
        Loan loan = NewLoan();
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id, accountId: account.Id, notes: "cash back"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = db.Transactions.Single();
        transaction.AccountId.Should().Be(account.Id);
        transaction.Direction.Should().Be(TransactionDirection.Expense);
        transaction.Description.Should().Be("Loan repayment to Parents");
        transaction.Amount.Amount.Should().Be(400m);
        transaction.Amount.Currency.Should().Be("EUR");
        transaction.TransactionDate.Should().Be(Today);
        transaction.IsTransfer.Should().BeTrue();
        transaction.IsAdjustment.Should().BeFalse();
        transaction.CategoryId.Should().Be(SeededCategories.LoanId);
        transaction.Source.Should().Be(TransactionSource.Manual);
        transaction.Notes.Should().Be("cash back");

        db.LoanPayments.Single().TransactionId.Should().Be(transaction.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GivenLoanWithAccount_SynthesizesIncomeRepayment()
    {
        Loan loan = NewLoan(direction: LoanDirection.Given, counterparty: "Ion", currency: "MDL");
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id, accountId: account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = db.Transactions.Single();
        transaction.Direction.Should().Be(TransactionDirection.Income);
        transaction.Description.Should().Be("Loan repayment from Ion");
        transaction.IsTransfer.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AmountOneCentOverOutstanding_ReturnsPaymentExceedsOutstanding()
    {
        Loan loan = NewLoan(principal: 1_000m);
        LoanPayment existing = NewPayment(loan.Id, 400m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [existing]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        // Outstanding is exactly 600.00 — one cent over must be rejected.
        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id, amount: 600.01m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentExceedsOutstanding);
        db.LoanPayments.Should().HaveCount(1);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AmountExactlyOutstanding_SettlesLoan()
    {
        Loan loan = NewLoan(principal: 1_000m);
        LoanPayment existing = NewPayment(loan.Id, 400m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [existing]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id, amount: 600m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.LoanPayments.Should().HaveCount(2);
        db.LoanPayments.Sum(p => p.Amount.Amount).Should().Be(loan.Principal.Amount);
    }

    [Fact]
    public async Task Handle_ArchivedLoan_ReturnsNotFound()
    {
        Loan loan = NewLoan();
        loan.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(loan.Id));
        db.LoanPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownLoan_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        var missingId = Guid.CreateVersion7();
        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_AccountCurrencyMismatch_FailsWithoutPersisting()
    {
        Loan loan = NewLoan(currency: "EUR");
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account], loans: [loan]);
        var handler = new RecordLoanPaymentCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            NewCommand(loan.Id, accountId: account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.AccountCurrencyMismatch);
        db.LoanPayments.Should().BeEmpty();
        db.Transactions.Should().BeEmpty();
    }
}
