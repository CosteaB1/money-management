using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;

/// <summary>
/// Read-only query summarizing transaction amounts (MDL) per category over a
/// date range, restricted to either Income or Expense.
/// </summary>
public sealed record GetCategoryBreakdownQuery(
    DateOnly From,
    DateOnly To,
    TransactionDirection Direction) : IQuery<CategoryBreakdownDto>;
