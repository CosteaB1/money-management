using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.ArchivePool;

internal sealed class ArchivePoolCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchivePoolCommand>
{
    public async Task<Result> Handle(ArchivePoolCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so the same command can re-archive (idempotent
        // contract). The default filter hides archived pools; without this a
        // second call would 404 instead of being a no-op. Mirrors the loan and
        // savings-goal archive handlers.
        Pool? pool = await db.Pools
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (pool is null)
        {
            return Result.Failure(PoolErrors.NotFound(command.Id));
        }

        // Counted from the ledger, not asserted by the caller: Pool.Archive
        // cannot query, so the write slice supplies the figure its invariant is
        // judged on.
        decimal outsideUnits = await PooledAccountGuard.OutsideUnitsAsync(db, pool.Id, cancellationToken);

        Result archive = pool.Archive(outsideUnits);
        if (archive.IsFailure)
        {
            return archive;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
