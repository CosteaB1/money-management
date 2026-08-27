using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Loans;

public static class LoanErrors
{
    public static readonly Error DirectionInvalid =
        Error.Validation("loans.direction_invalid", "Loan direction must be either Given or Received.");

    public static readonly Error CounterpartyRequired =
        Error.Validation("loans.counterparty_required", "Counterparty is required.");

    public static readonly Error CounterpartyTooLong =
        Error.Validation("loans.counterparty_too_long", "Counterparty must be 100 characters or fewer.");

    public static readonly Error PrincipalMustBePositive =
        Error.Validation("loans.principal_must_be_positive", "Principal amount must be greater than zero.");

    public static readonly Error InvalidCurrency =
        Error.Validation(
            "loans.invalid_currency",
            "Currency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

    public static readonly Error LoanDateInFuture =
        Error.Validation("loans.loan_date_in_future", "Loan date cannot be in the future.");

    public static readonly Error NotesTooLong =
        Error.Validation("loans.notes_too_long", "Notes must be 500 characters or fewer.");

    public static readonly Error PaymentMustBePositive =
        Error.Validation("loans.payment_must_be_positive", "Payment amount must be greater than zero.");

    public static readonly Error PaymentExceedsOutstanding =
        Error.Validation(
            "loans.payment_exceeds_outstanding",
            "Payment amount cannot exceed the loan's outstanding balance.");

    public static readonly Error PaymentCurrencyMismatch =
        Error.Validation(
            "loans.payment_currency_mismatch",
            "Payment currency must match the loan's currency (v1 is same-currency only).");

    public static readonly Error PaymentBeforeLoanDate =
        Error.Validation("loans.payment_before_loan_date", "Payment date cannot be earlier than the loan date.");

    public static readonly Error PaymentDateInFuture =
        Error.Validation("loans.payment_date_in_future", "Payment date cannot be in the future.");

    public static readonly Error AccountCurrencyMismatch =
        Error.Validation(
            "loans.account_currency_mismatch",
            "The selected account's currency must match the loan's currency.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("loans.not_found", $"Loan with id '{id}' was not found.");

    public static Error PaymentNotFound(Guid id) =>
        Error.NotFound("loans.payment_not_found", $"Loan payment with id '{id}' was not found.");
}
