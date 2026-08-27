using FluentAssertions;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Loans;

public class LoanTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock() => new FixedClock(FixedNow);

    private static Result<Loan> Create(
        LoanDirection direction = LoanDirection.Received,
        string counterparty = "Parents",
        decimal principal = 5_000m,
        string currency = "EUR",
        DateOnly? loanDate = null,
        string? notes = null) =>
        Loan.Create(
            direction,
            counterparty,
            new Money(principal, currency),
            loanDate ?? Today,
            notes,
            Clock());

    [Fact]
    public void Create_ValidReceivedLoan_Succeeds()
    {
        Result<Loan> result = Create(notes: "Borrowed for the apartment");

        result.IsSuccess.Should().BeTrue();
        Loan loan = result.Value;
        loan.Id.Should().NotBe(Guid.Empty);
        loan.Direction.Should().Be(LoanDirection.Received);
        loan.Counterparty.Should().Be("Parents");
        loan.Principal.Amount.Should().Be(5_000m);
        loan.Principal.Currency.Should().Be("EUR");
        loan.LoanDate.Should().Be(Today);
        loan.Notes.Should().Be("Borrowed for the apartment");
        loan.DisbursementTransactionId.Should().BeNull();
        loan.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Create_GivenDirection_Succeeds()
    {
        Result<Loan> result = Create(direction: LoanDirection.Given, counterparty: "Ion");

        result.IsSuccess.Should().BeTrue();
        result.Value.Direction.Should().Be(LoanDirection.Given);
    }

    [Fact]
    public void Create_UndefinedDirection_ReturnsDirectionInvalid()
    {
        Result<Loan> result = Create(direction: (LoanDirection)99);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.DirectionInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCounterparty_ReturnsCounterpartyRequired(string counterparty)
    {
        Result<Loan> result = Create(counterparty: counterparty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.CounterpartyRequired);
    }

    [Fact]
    public void Create_CounterpartyOver100Chars_ReturnsCounterpartyTooLong()
    {
        Result<Loan> result = Create(counterparty: new string('C', 101));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.CounterpartyTooLong);
    }

    [Fact]
    public void Create_CounterpartyExactly100CharsAfterTrim_Succeeds()
    {
        string counterparty = "  " + new string('C', 100) + "  ";

        Result<Loan> result = Create(counterparty: counterparty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Counterparty.Should().Be(new string('C', 100));
    }

    [Fact]
    public void Create_TrimsCounterparty()
    {
        Result<Loan> result = Create(counterparty: "  Parents  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Counterparty.Should().Be("Parents");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Create_NonPositivePrincipal_ReturnsPrincipalMustBePositive(decimal principal)
    {
        Result<Loan> result = Create(principal: principal);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.PrincipalMustBePositive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void Create_InvalidCurrency_ReturnsInvalidCurrency(string currency)
    {
        Result<Loan> result = Create(currency: currency);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.InvalidCurrency);
    }

    [Theory]
    [InlineData("MDL")]
    [InlineData("USD")]
    [InlineData("JPY")]
    public void Create_AnyValidIsoCurrency_Succeeds(string currency)
    {
        Result<Loan> result = Create(currency: currency);

        result.IsSuccess.Should().BeTrue();
        result.Value.Principal.Currency.Should().Be(currency);
    }

    [Fact]
    public void Create_FutureLoanDate_ReturnsLoanDateInFuture()
    {
        Result<Loan> result = Create(loanDate: Today.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.LoanDateInFuture);
    }

    [Fact]
    public void Create_TodayLoanDate_IsAllowed()
    {
        Result<Loan> result = Create(loanDate: Today);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_PastLoanDate_IsAllowed()
    {
        Result<Loan> result = Create(loanDate: Today.AddYears(-1));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_NotesOver500Chars_ReturnsNotesTooLong()
    {
        Result<Loan> result = Create(notes: new string('N', 501));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotesTooLong);
    }

    [Fact]
    public void Create_WhitespaceNotes_NormalizedToNull()
    {
        Result<Loan> result = Create(notes: "   ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_TrimsNotes()
    {
        Result<Loan> result = Create(notes: "  hello  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().Be("hello");
    }

    [Fact]
    public void Update_ValidInput_ChangesCounterpartyAndNotes()
    {
        Loan loan = Create().Value;

        Result result = loan.Update("  Grandparents  ", "  updated note  ");

        result.IsSuccess.Should().BeTrue();
        loan.Counterparty.Should().Be("Grandparents");
        loan.Notes.Should().Be("updated note");
    }

    [Fact]
    public void Update_NullNotes_ClearsNotes()
    {
        Loan loan = Create(notes: "original").Value;

        Result result = loan.Update("Parents", null);

        result.IsSuccess.Should().BeTrue();
        loan.Notes.Should().BeNull();
    }

    [Fact]
    public void Update_BlankCounterparty_FailsAndLeavesLoanUnchanged()
    {
        Loan loan = Create().Value;

        Result result = loan.Update("   ", "new note");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.CounterpartyRequired);
        loan.Counterparty.Should().Be("Parents");
    }

    [Fact]
    public void Update_NotesOver500Chars_ReturnsNotesTooLong()
    {
        Loan loan = Create().Value;

        Result result = loan.Update("Parents", new string('N', 501));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotesTooLong);
    }

    [Fact]
    public void Archive_SetsIsArchived()
    {
        Loan loan = Create().Value;

        Result result = loan.Archive();

        result.IsSuccess.Should().BeTrue();
        loan.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_AlreadyArchived_IsIdempotent()
    {
        Loan loan = Create().Value;
        loan.Archive();

        Result second = loan.Archive();

        second.IsSuccess.Should().BeTrue();
        loan.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_ArchivedLoan_ClearsIsArchived()
    {
        Loan loan = Create().Value;
        loan.Archive();

        Result result = loan.Unarchive();

        result.IsSuccess.Should().BeTrue();
        loan.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Unarchive_AlreadyActive_IsIdempotent()
    {
        Loan loan = Create().Value;
        loan.Archive();
        loan.Unarchive();

        Result second = loan.Unarchive();

        second.IsSuccess.Should().BeTrue();
        loan.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void SetDisbursementTransaction_LinksTransaction()
    {
        Loan loan = Create().Value;
        var transactionId = Guid.CreateVersion7();

        loan.SetDisbursementTransaction(transactionId);

        loan.DisbursementTransactionId.Should().Be(transactionId);
    }

    [Fact]
    public void ClearDisbursementTransaction_IsIdempotent()
    {
        Loan loan = Create().Value;
        loan.SetDisbursementTransaction(Guid.CreateVersion7());

        loan.ClearDisbursementTransaction();
        loan.ClearDisbursementTransaction();

        loan.DisbursementTransactionId.Should().BeNull();
    }
}
