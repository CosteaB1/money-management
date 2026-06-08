using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Transactions;

public static class TransferErrors
{
    public static readonly Error SameSourceAndDestination =
        Error.Validation(
            "transfer.same_source_and_destination",
            "Source and destination accounts must differ.");

    public static readonly Error MismatchedCurrencies =
        Error.Validation(
            "transfer.mismatched_currencies",
            "Source and destination accounts must share the same currency.");

    public static readonly Error DestinationAmountRequired =
        Error.Validation(
            "transfers.destination_amount_required",
            "A destination amount is required for cross-currency transfers.");

    public static Error SourceAccountNotFound(Guid id) =>
        Error.NotFound("transfer.source_account_not_found", $"Source account with id '{id}' was not found.");

    public static Error DestinationAccountNotFound(Guid id) =>
        Error.NotFound(
            "transfer.destination_account_not_found",
            $"Destination account with id '{id}' was not found.");
}
