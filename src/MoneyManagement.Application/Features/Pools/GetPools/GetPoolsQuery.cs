using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.GetPools;

/// <param name="IncludeArchived">
/// Opts archived pools back in, mirroring <c>GetLoansQuery</c>. Archiving a pool
/// requires zero outside units, so an archived pool is a finished story — worth
/// keeping listable, not worth showing by default.
/// </param>
public sealed record GetPoolsQuery(bool IncludeArchived = false) : IQuery<IReadOnlyList<PoolDto>>;
