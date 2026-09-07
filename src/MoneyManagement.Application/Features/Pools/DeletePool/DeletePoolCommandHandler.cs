using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.DeletePool;

internal sealed class DeletePoolCommandHandler(IApplicationDbContext db)
    : ICommandHandler<DeletePoolCommand>
{
    public async Task<Result> Handle(DeletePoolCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: the pools this command exists for are ARCHIVED
        // ones. Archiving was the only exit the slice offered, so every pool
        // that is stuck today is hidden behind the is_archived filter.
        Pool? pool = await db.Pools
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (pool is null)
        {
            return Result.Failure(PoolErrors.NotFound(command.Id));
        }

        // Guard 1 — somebody else's stake. Deliberately the SAME count
        // Pool.Archive is judged on (non-owner units, archived participants
        // included), so "deletable" can never be laxer than "archivable" on the
        // question that matters: is any of this money not the owner's?
        decimal outsideUnits = await PooledAccountGuard.OutsideUnitsAsync(db, pool.Id, cancellationToken);
        if (Math.Abs(outsideUnits) > PoolUnitEvent.UnitsDustTolerance)
        {
            return Result.Failure(PoolErrors.DeleteHasMovements);
        }

        // Materialized, not translated: Cash is a computed pairing of two scalar
        // columns and is Ignore()d in the model, so `e.Cash is not null` has no
        // SQL form. A pool's ledger is a handful of rows and they are all about
        // to be deleted anyway, so loading them costs nothing and keeps the
        // predicate readable in the language of the domain.
        List<PoolUnitEvent> events = await db.PoolUnitEvents
            .Where(e => e.PoolId == pool.Id)
            .ToListAsync(cancellationToken);

        // Guard 2 — money that actually moved. Cash means the event is one half
        // of a real transfer; a MovementTransactionId means a transaction row
        // points back here. Either way the ledger is a record of money, not a
        // typo, and the answer is archive rather than delete.
        bool movedMoney = events.Exists(e => e.Cash is not null || e.MovementTransactionId is not null);
        if (movedMoney)
        {
            return Result.Failure(PoolErrors.DeleteHasMovements);
        }

        List<PoolParticipant> participants = await db.PoolParticipants
            .Where(p => p.PoolId == pool.Id)
            .ToListAsync(cancellationToken);

        // Children first, then the parent. Both FKs are ON DELETE CASCADE so the
        // database would do this unaided, but stating it here is what makes the
        // behaviour testable against the Application suite's fake context - and
        // what stops a future migration quietly changing the answer.
        //
        // Transactions are NOT touched: see DeletePoolCommand's remarks. The
        // catch-up mark really did move the balance and may have been reconciled
        // against; reversing it silently would rewrite history.
        db.PoolUnitEvents.RemoveRange(events);
        db.PoolParticipants.RemoveRange(participants);
        db.Pools.Remove(pool);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
