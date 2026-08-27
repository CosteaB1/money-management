using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Loans;

/// <summary>
/// An interest-free personal loan the user gave to (or received from) a
/// counterparty, repaid over time in partial <see cref="LoanPayment"/>s.
/// <para>
/// The principal (amount + currency), direction, and loan date are immutable
/// after creation — changing any of them would corrupt the payment history
/// denominated against them, mirroring how <c>Account</c> currency is fixed at
/// creation. Only <see cref="Counterparty"/> and <see cref="Notes"/> are
/// user-editable.
/// </para>
/// <para>
/// <see cref="DisbursementTransactionId"/> optionally links the synthesized
/// money-movement row written when the loan was created against an account.
/// It is cleared (not cascaded) when that transaction is soft-deleted, so the
/// loan record itself always survives.
/// </para>
/// </summary>
public sealed class Loan : Entity
{
    public const int CounterpartyMaxLength = 100;
    public const int NotesMaxLength = 500;

    // EF Core
    private Loan()
    {
        Counterparty = string.Empty;
        Principal = Money.Zero(ReportingCurrencies.Mdl);
    }

    private Loan(
        Guid id,
        LoanDirection direction,
        string counterparty,
        Money principal,
        DateOnly loanDate,
        string? notes) : base(id)
    {
        Direction = direction;
        Counterparty = counterparty;
        Principal = principal;
        LoanDate = loanDate;
        Notes = notes;
        DisbursementTransactionId = null;
        IsArchived = false;
    }

    public LoanDirection Direction { get; private set; }
    public string Counterparty { get; private set; }
    public Money Principal { get; private set; }
    public DateOnly LoanDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid? DisbursementTransactionId { get; private set; }
    public bool IsArchived { get; private set; }

    public static Result<Loan> Create(
        LoanDirection direction,
        string counterparty,
        Money principal,
        DateOnly loanDate,
        string? notes,
        IDateTimeProvider clock)
    {
        if (!Enum.IsDefined(direction))
        {
            return Result.Failure<Loan>(LoanErrors.DirectionInvalid);
        }

        Result<string> counterpartyValidation = ValidateCounterparty(counterparty);
        if (counterpartyValidation.IsFailure)
        {
            return Result.Failure<Loan>(counterpartyValidation.Error);
        }

        if (principal.Amount <= 0m)
        {
            return Result.Failure<Loan>(LoanErrors.PrincipalMustBePositive);
        }

        if (!CurrencyCodes.IsValidIso(principal.Currency))
        {
            return Result.Failure<Loan>(LoanErrors.InvalidCurrency);
        }

        // No future-dated loans. Judged in UTC via the injected clock so the
        // comparison stays deterministic for tests — same convention as the
        // savings-goal contribution date guard.
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (loanDate > today)
        {
            return Result.Failure<Loan>(LoanErrors.LoanDateInFuture);
        }

        Result<string?> notesValidation = ValidateNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<Loan>(notesValidation.Error);
        }

        return new Loan(
            Guid.CreateVersion7(),
            direction,
            counterpartyValidation.Value,
            principal,
            loanDate,
            notesValidation.Value);
    }

    /// <summary>
    /// Edits the user-mutable metadata. Counterparty and notes only —
    /// principal, currency, direction, and loan date are immutable after
    /// creation (the payment history is denominated against them).
    /// </summary>
    public Result Update(string counterparty, string? notes)
    {
        Result<string> counterpartyValidation = ValidateCounterparty(counterparty);
        if (counterpartyValidation.IsFailure)
        {
            return Result.Failure(counterpartyValidation.Error);
        }

        Result<string?> notesValidation = ValidateNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure(notesValidation.Error);
        }

        Counterparty = counterpartyValidation.Value;
        Notes = notesValidation.Value;
        return Result.Success();
    }

    /// <summary>Idempotent: archiving an already-archived loan is a no-op.</summary>
    public Result Archive()
    {
        IsArchived = true;
        return Result.Success();
    }

    /// <summary>Idempotent: unarchiving an already-active loan is a no-op.</summary>
    public Result Unarchive()
    {
        IsArchived = false;
        return Result.Success();
    }

    /// <summary>
    /// Links the synthesized disbursement transaction written when the loan
    /// was created against an account.
    /// </summary>
    public void SetDisbursementTransaction(Guid transactionId) =>
        DisbursementTransactionId = transactionId;

    /// <summary>
    /// Idempotent: drops the disbursement link when the underlying transaction
    /// is (soft-)deleted, so the loan never points at a dead row.
    /// </summary>
    public void ClearDisbursementTransaction() => DisbursementTransactionId = null;

    private static Result<string> ValidateCounterparty(string counterparty)
    {
        if (string.IsNullOrWhiteSpace(counterparty))
        {
            return Result.Failure<string>(LoanErrors.CounterpartyRequired);
        }

        string trimmed = counterparty.Trim();
        if (trimmed.Length > CounterpartyMaxLength)
        {
            return Result.Failure<string>(LoanErrors.CounterpartyTooLong);
        }

        return Result.Success(trimmed);
    }

    private static Result<string?> ValidateNotes(string? notes)
    {
        string? trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > NotesMaxLength)
        {
            return Result.Failure<string?>(LoanErrors.NotesTooLong);
        }

        return Result.Success<string?>(trimmed);
    }
}
