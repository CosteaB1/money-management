using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.UnarchivePool;

internal sealed class UnarchivePoolCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UnarchivePoolCommand>
{
    public async Task<Result> Handle(UnarchivePoolCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters is the whole point: the default filter hides
        // archived pools, and unarchive must find exactly those rows. An
        // already-active pool is an idempotent success, not a 404 - the archive
        // handler's contract, read backwards.
        Pool? pool = await db.Pools
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (pool is null)
        {
            return Result.Failure(PoolErrors.NotFound(command.Id));
        }

        // No outside-units check, and that asymmetry is deliberate: Archive has
        // to count them because it RELEASES the account back to the guards'
        // "not pooled" branch. Unarchive re-arms those guards, so it can only
        // make the account safer.
        Result unarchive = pool.Unarchive();
        if (unarchive.IsFailure)
        {
            return unarchive;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
