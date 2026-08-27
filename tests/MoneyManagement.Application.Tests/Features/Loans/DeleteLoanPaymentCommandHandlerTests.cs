using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Loans.DeleteLoanPayment;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class DeleteLoanPaymentCommandHandlerTests
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

    private static LoanPayment NewPayment(Guid loanId, decimal amount = 400m, string currency = "EUR")
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

    private static Transaction NewTransaction(decimal amount = 400m, string currency = "EUR")
    {
        Result<Transaction> result = Transaction.Create(
            Guid.CreateVersion7(),
            Today,
            TransactionDirection.Expense,
            new Money(amount, currency),
            "Loan repayment to Parents",
            TransactionSource.Manual,
            categoryId: null,
            isTransfer: true);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = result.Value;
        transaction.ClearDomainEvents();
        return transaction;
    }

    [Fact]
    public async Task Handle_UnlinkedPayment_RemovesRow()
    {
        var loanId = Guid.CreateVersion7();
        LoanPayment payment = NewPayment(loanId);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loanPayments: [payment]);
        var handler = new DeleteLoanPaymentCommandHandler(db, FakeFxConverter.Identity());

        Result result = await handler.Handle(
            new DeleteLoanPaymentCommand(loanId, payment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.LoanPayments.Should().BeEmpty();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LinkedPayment_SoftDeletesTransactionWithFxAmount()
    {
        var loanId = Guid.CreateVersion7();
        Transaction transaction = NewTransaction(amount: 400m, currency: "EUR");
        LoanPayment payment = NewPayment(loanId);
        payment.LinkTransaction(transaction.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [transaction],
            loanPayments: [payment]);
        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal> { ["EUR"] = 19.5m });
        var handler = new DeleteLoanPaymentCommandHandler(db, fx);

        Result result = await handler.Handle(
            new DeleteLoanPaymentCommand(loanId, payment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.IsDeleted.Should().BeTrue();
        db.LoanPayments.Should().BeEmpty();

        // The deleted event must carry the row-date FX conversion so the
        // budget-inverse and loan event handlers see the booked MDL value.
        TransactionDeletedDomainEvent deletedEvent = transaction.GetDomainEvents()
            .OfType<TransactionDeletedDomainEvent>()
            .Single();
        deletedEvent.AmountMdl.Should().Be(400m * 19.5m);
        await fx.Received(1).ConvertAsync(
            400m, "EUR", "MDL", Today, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LinkedTransactionAlreadyDeleted_SkipsAndStillRemovesPayment()
    {
        var loanId = Guid.CreateVersion7();
        Transaction transaction = NewTransaction();
        transaction.MarkDeleted();
        transaction.ClearDomainEvents();

        LoanPayment payment = NewPayment(loanId);
        payment.LinkTransaction(transaction.Id);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [transaction],
            loanPayments: [payment]);
        var handler = new DeleteLoanPaymentCommandHandler(db, FakeFxConverter.Identity());

        Result result = await handler.Handle(
            new DeleteLoanPaymentCommand(loanId, payment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.LoanPayments.Should().BeEmpty();
        // MarkDeleted is idempotent — no second delete event is raised.
        transaction.GetDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PaymentUnderDifferentLoan_ReturnsPaymentNotFound()
    {
        var actualLoanId = Guid.CreateVersion7();
        LoanPayment payment = NewPayment(actualLoanId);
        IApplicationDbContext db = FakeApplicationDbContext.Create(loanPayments: [payment]);
        var handler = new DeleteLoanPaymentCommandHandler(db, FakeFxConverter.Identity());

        var wrongLoanId = Guid.CreateVersion7();
        Result result = await handler.Handle(
            new DeleteLoanPaymentCommand(wrongLoanId, payment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentNotFound(payment.Id));
        db.LoanPayments.Should().HaveCount(1);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownPayment_ReturnsPaymentNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new DeleteLoanPaymentCommandHandler(db, FakeFxConverter.Identity());

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(
            new DeleteLoanPaymentCommand(Guid.CreateVersion7(), missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentNotFound(missingId));
    }
}
