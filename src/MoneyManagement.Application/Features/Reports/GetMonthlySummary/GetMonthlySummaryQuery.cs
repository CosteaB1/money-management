using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Reports.GetMonthlySummary;

/// <summary>
/// Read-only multi-month income/expense series.
/// </summary>
/// <param name="From">
/// Inclusive start month. The handler keys off year+month only. When null the
/// handler defaults to the trailing 12 months ending at the current UTC month.
/// </param>
/// <param name="To">Inclusive end month. Null defaults alongside <paramref name="From"/>.</param>
public sealed record GetMonthlySummaryQuery(DateOnly? From = null, DateOnly? To = null)
    : IQuery<IReadOnlyList<MonthlySummaryPointDto>>;
