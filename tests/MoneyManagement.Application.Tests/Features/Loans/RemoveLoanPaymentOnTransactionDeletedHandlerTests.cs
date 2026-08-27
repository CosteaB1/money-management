using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Loans.EventHandlers;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

/// <summary>
/// Behavior tests for the loan-side cleanup when a loan-linked transaction is
/// soft-deleted from the transactions page. Mirrors the budget event handler
/// tests' shape: skip rules, the mutation paths, and the no-op paths.
/// </summary>
public class RemoveLoanPaymentOnTransactionDeletedHandlerTests
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

    private static Loan NewLoan()
    {
        Result<Loan> result = Loan.Create(
            LoanDirection.Received,
            "Parents",
            new Money(5_000m, "EUR"),
            LoanDate,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static LoanPayment NewPayment(Guid loanId, Guid? transactionId = null)
    {
        Result<LoanPayment> result = LoanPayment.Create(
            loanId,
            new Money(500m, "EUR"),
            "EUR",
            LoanDate,
            Today,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        LoanPayment payment = result.Value;
        if (transactionId is Guid txId)
        {
            payment.LinkTransaction(txId);
        }

        return payment;
    }

    private static TransactionDeletedDomainEvent NewEvent(
        Guid? transactionId = null,
        bool isTransfer = true) => new(
            TransactionId: transactionId ?? Guid.CreateVersion7(),
            CategoryId: null,
            TransactionDate: Today,
            AmountMdl: 100m,
            Direction: TransactionDirection.Expense,
            IsTransfer: isTransfer,
            IsAdjustment: false);

    private static RemoveLoanPaymentOnTransactionDeletedHandler NewHandler(IApplicationDbContext db) =>
        new(db, NullLogger<RemoveLoanPaymentOnTransactionDeletedHandler>.Instance);

    [Fact]
    public async Task Handle_RemovesPaymentLinkedToDeletedTransaction()
    {
        var transactionId = Guid.CreateVersion7();
        Loan loan = NewLoan();
        LoanPayment linked = NewPayment(loan.Id, transactionId);
        LoanPayment unrelated = NewPayment(loan.Id, Guid.CreateVersion7());

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [linked, unrelated]);
        RemoveLoanPaymentOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(transactionId), CancellationToken.None);

        db.LoanPayments.Should().ContainSingle(p => p.Id == unrelated.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClearsDisbursementLinkOnMatchingLoan()
    {
        var transactionId = Guid.CreateVersion7();
        Loan loan = NewLoan();
        loan.SetDisbursementTransaction(transactionId);

        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        RemoveLoanPaymentOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(transactionId), CancellationToken.None);

        loan.DisbursementTransactionId.Should().BeNull();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClearsDisbursementLinkOnArchivedLoanToo()
    {
        var transactionId = Guid.CreateVersion7();
        Loan loan = NewLoan();
        loan.SetDisbursementTransaction(transactionId);
        loan.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        RemoveLoanPaymentOnTransactionDeletedHandler handler = NewHandler(db);

        await handler.Handle(NewEvent(transactionId), CancellationToken.None);

        loan.DisbursementTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonTransferEvent_IsIgnored()
    {
        var transactionId = Guid.CreateVersion7();
        Loan loan = NewLoan();
        loan.SetDisbursementTransaction(transactionId);
        LoanPayment linked = NewPayment(loan.Id, transactionId);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [linked]);
        RemoveLoanPaymentOnTransactionDeletedHandler handler = NewHandler(db);

        // Loan-synthesized rows are always transfer-flagged; a non-transfer
        // deletion can never be loan-linked, so the handler must fast-exit.
        await handler.Handle(NewEvent(transactionId, isTransfer: false), CancellationToken.None);

        db.LoanPayments.Should().HaveCount(1);
        loan.DisbursementTransactionId.Should().Be(transactionId);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoMatchingPaymentOrLoan_IsNoOpWithoutSave()
    {
        Loan loan = NewLoan();
        LoanPayment payment = NewPayment(loan.Id, Guid.CreateVersion7());

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            loans: [loan],
            loanPayments: [payment]);
        RemoveLoanPaymentOnTransactionDeletedHandler handler = NewHandler(db);

        // The DeleteLoanPaymentCommand path: the payment row is already gone
        // by the time this event fires, so the handler must no-op silently.
        await handler.Handle(NewEvent(), CancellationToken.None);

        db.LoanPayments.Should().HaveCount(1);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
