using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Reports.GetTopPayees;

public sealed record GetTopPayeesQuery(
    DateOnly From,
    DateOnly To,
    TransactionDirection Direction,
    int Limit = 10) : IQuery<IReadOnlyList<TopPayeeDto>>;
