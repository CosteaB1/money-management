using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.GetPoolDetail;

public sealed record GetPoolDetailQuery(Guid Id) : IQuery<PoolDetailDto>;
