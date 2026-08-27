using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Loans;

/// <summary>
/// A single partial repayment against a <see cref="Loan"/>. Always positive
/// and denominated in the loan's currency (v1 is same-currency only — no FX
/// between the payment and its loan).
/// <para>
/// <see cref="TransactionId"/> optionally links the synthesized money-movement
/// row written when the payment was recorded against an account. When that
/// transaction is soft-deleted from the transactions page, the payment row is
/// removed by <c>RemoveLoanPaymentOnTransactionDeletedHandler</c> so the two
/// sides never disagree.
/// </para>
/// </summary>
public sealed class LoanPayment : Entity
{
    public const int NotesMaxLength = 500;

    // EF Core
    private LoanPayment()
    {
        Amount = Money.Zero(ReportingCurrencies.Mdl);
    }

    private LoanPayment(
        Guid id,
        Guid loanId,
        Money amount,
        DateOnly occurredOn,
        string? notes) : base(id)
    {
        LoanId = loanId;
        Amount = amount;
        OccurredOn = occurredOn;
        TransactionId = null;
        Notes = notes;
    }

    public Guid LoanId { get; private set; }
    public Money Amount { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public Guid? TransactionId { get; private set; }
    public string? Notes { get; private set; }

    public static Result<LoanPayment> Create(
        Guid loanId,
        Money amount,
        string loanCurrency,
        DateOnly loanDate,
        DateOnly occurredOn,
        string? notes,
        IDateTimeProvider clock)
    {
        if (loanId == Guid.Empty)
        {
            return Result.Failure<LoanPayment>(LoanErrors.NotFound(loanId));
        }

        if (amount.Amount <= 0m)
        {
            return Result.Failure<LoanPayment>(LoanErrors.PaymentMustBePositive);
        }

        if (!string.Equals(amount.Currency, loanCurrency, StringComparison.Ordinal))
        {
            return Result.Failure<LoanPayment>(LoanErrors.PaymentCurrencyMismatch);
        }

        // No future-dated payments; judged in UTC via the injected clock —
        // same convention as the loan-date guard.
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (occurredOn > today)
        {
            return Result.Failure<LoanPayment>(LoanErrors.PaymentDateInFuture);
        }

        if (occurredOn < loanDate)
        {
            return Result.Failure<LoanPayment>(LoanErrors.PaymentBeforeLoanDate);
        }

        string? trimmedNotes = notes?.Trim();
        if (string.IsNullOrEmpty(trimmedNotes))
        {
            trimmedNotes = null;
        }
        else if (trimmedNotes.Length > NotesMaxLength)
        {
            return Result.Failure<LoanPayment>(LoanErrors.NotesTooLong);
        }

        return new LoanPayment(Guid.CreateVersion7(), loanId, amount, occurredOn, trimmedNotes);
    }

    /// <summary>
    /// Links the synthesized money-movement transaction written when the
    /// payment was recorded against an account.
    /// </summary>
    public void LinkTransaction(Guid transactionId) => TransactionId = transactionId;
}
