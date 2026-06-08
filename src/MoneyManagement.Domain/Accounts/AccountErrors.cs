using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Accounts;

public static class AccountErrors
{
    public static readonly Error NameRequired =
        Error.Validation("account.name_required", "Account name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("account.name_too_long", "Account name must be 100 characters or fewer.");

    public static readonly Error NegativeBalanceForNonCreditCard =
        Error.Validation(
            "account.negative_balance_for_non_credit_card",
            "Balance can be negative only for Credit Card accounts.");

    public static readonly Error InvalidCurrency =
        Error.Validation(
            "account.invalid_currency",
            "Currency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

    public static Error NotFound(Guid id) =>
        Error.NotFound("account.not_found", $"Account with id '{id}' was not found.");

    public static Error IsArchived(Guid id) =>
        Error.Validation("account.is_archived", $"Account with id '{id}' is archived.");

    public static Error HasLinkedRecords(Guid id) =>
        Error.Conflict(
            "account.has_linked_records",
            $"Account with id '{id}' has linked transactions, imports, or goals and can't be permanently deleted. Archive it instead.");
}
