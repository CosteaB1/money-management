using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;

/// <summary>
/// Top-level wrapper for the category breakdown. <see cref="Items"/> are sorted
/// by <see cref="CategoryBreakdownItemDto.AmountMdl"/> descending.
/// <see cref="MissingFxRate"/> is true when at least one row was omitted from
/// the aggregate because it had no FX-convertible amount at its date.
/// </summary>
public sealed record CategoryBreakdownDto(
    DateOnly From,
    DateOnly To,
    TransactionDirection Direction,
    decimal TotalMdl,
    bool MissingFxRate,
    IReadOnlyList<CategoryBreakdownItemDto> Items);
