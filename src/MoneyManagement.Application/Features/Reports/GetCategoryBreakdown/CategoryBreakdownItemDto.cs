namespace MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;

/// <summary>
/// One bucket of the category breakdown. <see cref="CategoryId"/> is null for
/// the synthetic "Uncategorized" bucket. <see cref="Percentage"/> is in
/// <c>[0, 1]</c> against the report total (not 0..100).
/// </summary>
public sealed record CategoryBreakdownItemDto(
    Guid? CategoryId,
    string CategoryName,
    decimal AmountMdl,
    decimal Percentage,
    int TransactionCount);
