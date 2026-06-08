using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Dashboard.GetSummary;

/// <summary>
/// Read-only query for a single calendar month's income/expense summary.
/// </summary>
/// <param name="Month">
/// Optional anchor month. When null the handler resolves it to the current
/// UTC month (via <see cref="MoneyManagement.SharedKernel.IDateTimeProvider"/>).
/// The <see cref="DateOnly"/>'s day component is irrelevant — only year and
/// month are used to build the window.
/// </param>
public sealed record GetSummaryQuery(DateOnly? Month = null)
    : IQuery<DashboardSummaryDto>;
