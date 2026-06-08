using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Imports;

public static class ImportBatchErrors
{
    public static readonly Error AccountRequired =
        Error.Validation("import_batch.account_required", "Account id is required.");

    public static readonly Error FileNameRequired =
        Error.Validation("import_batch.file_name_required", "File name is required.");

    public static readonly Error FileNameTooLong =
        Error.Validation("import_batch.file_name_too_long", "File name must be 260 characters or fewer.");

    public static readonly Error FileHashRequired =
        Error.Validation("import_batch.file_hash_required", "File hash is required.");

    public static readonly Error FileHashTooLong =
        Error.Validation("import_batch.file_hash_too_long", "File hash must be 64 characters or fewer.");

    public static readonly Error UnsupportedFormat =
        Error.Validation("imports.unsupported_format", "The provided file is not a supported bank statement format.");

    public static readonly Error ParseFailed =
        Error.Failure("imports.parse_failed", "Failed to parse the bank statement.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("import_batch.not_found", $"Import batch with id '{id}' was not found.");
}
