using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Dashboard.GetNetWorth;

/// <summary>
/// Read-only query for the live net-worth card. Takes no parameters: the card
/// is always "as of today", and the historical view is
/// <c>GetNetWorthTrendQuery</c>'s job.
/// </summary>
public sealed record GetNetWorthQuery : IQuery<NetWorthDto>;
