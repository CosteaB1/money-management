using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.ArchiveAccount;

internal sealed class ArchiveAccountCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchiveAccountCommand>
{
    public async Task<Result> Handle(ArchiveAccountCommand command, CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure(AccountErrors.NotFound(command.Id));
        }

        // An archived account drops out of the default queries, which reverts
        // the pool's owner fraction to 1.0 and silently reabsorbs the friends'
        // stake into the user's net worth — with no event and no audit trail.
        // Same reasoning as Pool.Archive and PoolParticipant.Archive; the units
        // must be redeemed first.
        Pool? pool = await PooledAccountGuard.FindPoolAsync(db, account.Id, cancellationToken);
        if (pool is not null)
        {
            decimal outsideUnits = await PooledAccountGuard.OutsideUnitsAsync(db, pool.Id, cancellationToken);

            if (Math.Abs(outsideUnits) > PoolUnitEvent.UnitsDustTolerance)
            {
                return Result.Failure(PoolErrors.AccountArchiveBlocked);
            }
        }

        account.Archive();
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
