namespace MoneyManagement.Application.Features.Reports.GetTopPayees;

/// <summary>
/// One top-payee bucket. <see cref="Payee"/> is the normalized grouping key
/// (trimmed + lowercased description); <see cref="OriginalDescription"/> is
/// the raw description from the earliest transaction in the bucket, kept for
/// readable display.
/// </summary>
public sealed record TopPayeeDto(
    string Payee,
    string OriginalDescription,
    decimal AmountMdl,
    int TransactionCount);
