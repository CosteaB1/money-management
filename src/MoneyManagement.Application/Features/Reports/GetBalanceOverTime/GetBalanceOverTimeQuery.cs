using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Reports.GetBalanceOverTime;

public sealed record GetBalanceOverTimeQuery(
    Guid AccountId,
    DateOnly From,
    DateOnly To,
    BalanceInterval Interval = BalanceInterval.Monthly)
    : IQuery<IReadOnlyList<BalancePointDto>>;
