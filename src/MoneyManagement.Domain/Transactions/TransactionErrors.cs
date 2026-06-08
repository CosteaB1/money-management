using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Transactions;

public static class TransactionErrors
{
    public static readonly Error AccountRequired =
        Error.Validation("transaction.account_required", "Account id is required.");

    public static readonly Error DescriptionRequired =
        Error.Validation("transaction.description_required", "Description is required.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("transaction.description_too_long", "Description must be 500 characters or fewer.");

    public static readonly Error NotesTooLong =
        Error.Validation("transactions.notes_too_long", "Notes must be 500 characters or fewer.");

    public static readonly Error AmountNotPositive =
        Error.Validation("transaction.amount_not_positive", "Amount must be greater than zero.");

    public static readonly Error InvalidCurrency =
        Error.Validation(
            "transaction.invalid_currency",
            "Transaction currency must be a 3-letter uppercase ISO code.");

    public static readonly Error DateInFuture =
        Error.Validation("transaction.date_in_future", "Transaction date cannot be in the future.");

    public static readonly Error InvalidDirection =
        Error.Validation("transaction.invalid_direction", "Transaction direction is not a valid value.");

    public static readonly Error InvalidSource =
        Error.Validation("transaction.invalid_source", "Transaction source is not a valid value.");

    public static readonly Error InvalidOriginalCurrency =
        Error.Validation("transaction.invalid_original_currency", "Original currency must be a 3-letter code.");

    public static readonly Error OriginalAmountNotPositive =
        Error.Validation("transaction.original_amount_not_positive", "Original amount must be greater than zero when set.");

    public static readonly Error CounterAccountWithoutTransferFlag =
        Error.Validation(
            "transaction.counter_account_without_transfer_flag",
            "Counter account id can only be set when the transaction is flagged as a transfer.");

    public static readonly Error CounterAccountCannotBeSelf =
        Error.Validation(
            "transaction.counter_account_cannot_be_self",
            "Counter account id must differ from the transaction's own account.");

    public static readonly Error TransferAndAdjustmentAreMutuallyExclusive =
        Error.Validation(
            "transaction.transfer_and_adjustment_are_mutually_exclusive",
            "A transaction cannot be both a transfer and a balance adjustment.");

    public static readonly Error CurrencyMismatchAccount =
        Error.Validation(
            "transaction.currency_mismatch_account",
            "Transaction currency must match the account's currency.");

    public static readonly Error TransferCurrencyMismatch =
        Error.Validation(
            "transaction.transfer_currency_mismatch",
            "Counter account currency must match the import account's currency.");

    public static readonly Error CounterAmountRequired =
        Error.Validation(
            "transactions.counter_amount_required",
            "A counter amount is required when the counter account is a different currency.");

    public static readonly Error AdjustmentDeltaZero =
        Error.Validation(
            "transaction.adjustment_delta_zero",
            "New balance equals the current balance; no adjustment to record.");

    public static Error AdjustmentAccountTypeNotEligible(string accountType) =>
        Error.Validation(
            "transaction.adjustment_account_type_not_eligible",
            $"account.type {accountType} does not support balance adjustments.");

    public static readonly Error CategoryFlowMismatch =
        Error.Validation(
            "transaction.category_flow_mismatch",
            "The category's flow is not compatible with the transaction's direction.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("transaction.not_found", $"Transaction with id '{id}' was not found.");
}
