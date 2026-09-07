using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// The one place that answers "is this account pooled?".
/// <para>
/// Net worth reads a pooled account as <c>value × ownerFraction</c>, so <b>any
/// cash landing on it that does not mint or burn units is silently shared
/// pro-rata with the outside investors.</b> Every write path that can move
/// money on an account therefore asks this first. A guard added later is a
/// guard added after real money moved wrong.
/// </para>
/// <para>
/// <b>Pooled means a NON-ARCHIVED pool exists for the account.</b> Archiving a
/// pool already requires zero outside units (<see cref="Pool.Archive"/>), so an
/// archived pool's account is safely a normal account again and the guards must
/// let go of it. The <c>Pool</c> query filter already excludes archived rows;
/// the explicit <c>!IsArchived</c> predicate is defense-in-depth for unit tests
/// that bypass model configuration.
/// </para>
/// <para>
/// <b>No N+1.</b> Single-account callers do one indexed <c>EXISTS</c> against
/// the unique <c>ix_pools_account_id</c>; multi-account callers (transfers,
/// import commits) pass every id they touch to
/// <see cref="PooledAsync"/> and get the pooled subset back in ONE round trip.
/// Nothing iterates accounts issuing a query each.
/// </para>
/// <para>
/// The pool's own commands never call these — they write their transactions
/// inline rather than delegating to the guarded handlers, so the sanctioned
/// path bypasses the guard structurally rather than by a flag somebody can
/// forget to set.
/// </para>
/// </summary>
internal static class PooledAccountGuard
{
    /// <summary>True when a non-archived pool sits on <paramref name="accountId"/>.</summary>
    public static Task<bool> IsPooledAsync(
        IApplicationDbContext db,
        Guid accountId,
        CancellationToken cancellationToken) =>
        db.Pools.AnyAsync(p => p.AccountId == accountId && !p.IsArchived, cancellationToken);

    /// <summary>
    /// The subset of <paramref name="accountIds"/> that is pooled, in one query.
    /// Empty input short-circuits without touching the database.
    /// </summary>
    public static async Task<HashSet<Guid>> PooledAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        List<Guid> pooled = await db.Pools
            .Where(p => accountIds.Contains(p.AccountId) && !p.IsArchived)
            .Select(p => p.AccountId)
            .ToListAsync(cancellationToken);

        return [.. pooled];
    }

    /// <summary>
    /// The non-archived pool on <paramref name="accountId"/>, or <c>null</c>.
    /// Used by the guards that need to look past the pool at its unit ledger
    /// (delete-transaction, archive-account) rather than just knowing it exists.
    /// </summary>
    public static Task<Pool?> FindPoolAsync(
        IApplicationDbContext db,
        Guid accountId,
        CancellationToken cancellationToken) =>
        db.Pools.FirstOrDefaultAsync(p => p.AccountId == accountId && !p.IsArchived, cancellationToken);

    /// <summary>
    /// True when soft-deleting the given transaction would re-price units
    /// already issued to a third party.
    /// <para>
    /// Two cases: the row IS a unit event's movement leg, or it is dated
    /// on/before the pool's latest unit event, so removing it changes the
    /// balance every NAV since was struck from. The sanctioned undo is
    /// <c>DELETE /pools/{id}/events/{eventId}</c>, which drops the units and the
    /// money together.
    /// </para>
    /// <para>
    /// Lives here rather than in <c>DeleteTransactionCommandHandler</c> because
    /// that handler is NOT the only path that soft-deletes a row:
    /// <c>DeleteLoanPayment</c> does it inline. Two copies of a money-safety
    /// rule is how one of them silently stops matching the other.
    /// </para>
    /// <para>
    /// Costs one indexed <c>EXISTS</c> on non-pooled accounts - the 99.9% case -
    /// and two more only when the account really is pooled.
    /// </para>
    /// </summary>
    public static async Task<bool> DeleteRepricesUnitsAsync(
        IApplicationDbContext db,
        Guid accountId,
        Guid transactionId,
        DateOnly transactionDate,
        CancellationToken cancellationToken)
    {
        Pool? pool = await FindPoolAsync(db, accountId, cancellationToken);
        if (pool is null)
        {
            return false;
        }

        bool referencedByUnitEvent = await db.PoolUnitEvents
            .AnyAsync(e => e.MovementTransactionId == transactionId, cancellationToken);

        if (referencedByUnitEvent)
        {
            return true;
        }

        DateOnly? latestUnitEvent = await db.PoolUnitEvents
            .Where(e => e.PoolId == pool.Id)
            .Select(e => (DateOnly?)e.OccurredOn)
            .MaxAsync(cancellationToken);

        return latestUnitEvent is DateOnly latest && transactionDate <= latest;
    }

    /// <summary>
    /// Units held by NON-owner participants of <paramref name="poolId"/>, all
    /// dates. One grouped query; archived participants are deliberately
    /// included, because their units are still somebody else's money.
    /// </summary>
    public static async Task<decimal> OutsideUnitsAsync(
        IApplicationDbContext db,
        Guid poolId,
        CancellationToken cancellationToken)
    {
        List<Guid> outsideParticipantIds = await db.PoolParticipants
            .Where(p => p.PoolId == poolId && !p.IsOwner)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (outsideParticipantIds.Count == 0)
        {
            return 0m;
        }

        List<PoolUnitEvent> events = await db.PoolUnitEvents
            .Where(e => e.PoolId == poolId && outsideParticipantIds.Contains(e.ParticipantId))
            .ToListAsync(cancellationToken);

        decimal units = 0m;
        foreach (PoolUnitEvent unitEvent in events)
        {
            units += unitEvent.UnitsDelta;
        }

        return units;
    }
}
