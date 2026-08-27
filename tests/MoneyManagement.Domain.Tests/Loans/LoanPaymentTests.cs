using FluentAssertions;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Loans;

public class LoanPaymentTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly LoanDate = new(2026, 1, 15);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock() => new FixedClock(FixedNow);

    private static Result<LoanPayment> Create(
        Guid? loanId = null,
        decimal amount = 500m,
        string currency = "EUR",
        string loanCurrency = "EUR",
        DateOnly? loanDate = null,
        DateOnly? occurredOn = null,
        string? notes = null) =>
        LoanPayment.Create(
            loanId ?? Guid.CreateVersion7(),
            new Money(amount, currency),
            loanCurrency,
            loanDate ?? LoanDate,
            occurredOn ?? Today,
            notes,
            Clock());

    [Fact]
    public void Create_ValidPayment_Succeeds()
    {
        var loanId = Guid.CreateVersion7();

        Result<LoanPayment> result = Create(loanId: loanId, notes: "First installment");

        result.IsSuccess.Should().BeTrue();
        LoanPayment payment = result.Value;
        payment.Id.Should().NotBe(Guid.Empty);
        payment.LoanId.Should().Be(loanId);
        payment.Amount.Amount.Should().Be(500m);
        payment.Amount.Currency.Should().Be("EUR");
        payment.OccurredOn.Should().Be(Today);
        payment.TransactionId.Should().BeNull();
        payment.Notes.Should().Be("First installment");
    }

    [Fact]
    public void Create_EmptyLoanId_ReturnsNotFound()
    {
        Result<LoanPayment> result = Create(loanId: Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(Guid.Empty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-250)]
    public void Create_NonPositiveAmount_ReturnsPaymentMustBePositive(decimal amount)
    {
        Result<LoanPayment> result = Create(amount: amount);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentMustBePositive);
    }

    [Fact]
    public void Create_CurrencyDiffersFromLoanCurrency_ReturnsPaymentCurrencyMismatch()
    {
        Result<LoanPayment> result = Create(currency: "MDL", loanCurrency: "EUR");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentCurrencyMismatch);
    }

    [Fact]
    public void Create_OccurredOnBeforeLoanDate_ReturnsPaymentBeforeLoanDate()
    {
        Result<LoanPayment> result = Create(occurredOn: LoanDate.AddDays(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentBeforeLoanDate);
    }

    [Fact]
    public void Create_OccurredOnEqualToLoanDate_IsAllowed()
    {
        Result<LoanPayment> result = Create(occurredOn: LoanDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.OccurredOn.Should().Be(LoanDate);
    }

    [Fact]
    public void Create_FutureOccurredOn_ReturnsPaymentDateInFuture()
    {
        Result<LoanPayment> result = Create(occurredOn: Today.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PaymentDateInFuture);
    }

    [Fact]
    public void Create_TodayOccurredOn_IsAllowed()
    {
        Result<LoanPayment> result = Create(occurredOn: Today);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_NotesOver500Chars_ReturnsNotesTooLong()
    {
        Result<LoanPayment> result = Create(notes: new string('N', 501));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotesTooLong);
    }

    [Fact]
    public void Create_WhitespaceNotes_NormalizedToNull()
    {
        Result<LoanPayment> result = Create(notes: "   ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_TrimsNotes()
    {
        Result<LoanPayment> result = Create(notes: "  cash handover  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().Be("cash handover");
    }

    [Fact]
    public void LinkTransaction_SetsTransactionId()
    {
        LoanPayment payment = Create().Value;
        var transactionId = Guid.CreateVersion7();

        payment.LinkTransaction(transactionId);

        payment.TransactionId.Should().Be(transactionId);
    }
}
