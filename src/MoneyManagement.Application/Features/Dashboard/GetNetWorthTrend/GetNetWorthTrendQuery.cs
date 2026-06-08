using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;

/// <summary>
/// Read-only query for a rolling net-worth trend.
/// </summary>
/// <param name="Months">
/// Total number of points to return, including the live "today" point. The
/// handler validates this is in <c>[1, 24]</c> and returns
/// <see cref="DashboardErrors.MonthsOutOfRange"/> otherwise.
/// </param>
public sealed record GetNetWorthTrendQuery(int Months = 6)
    : IQuery<IReadOnlyList<NetWorthTrendPointDto>>;
