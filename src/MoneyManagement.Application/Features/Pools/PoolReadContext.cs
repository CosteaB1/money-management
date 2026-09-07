using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Everything the pool READ queries need, loaded once for a whole set of pools:
/// their accounts, their full rosters, their full ledgers and the shared
/// balance ledger.
/// <para>
/// The read counterpart of <see cref="PoolWriteContext"/>, and separate from it
/// on purpose. The write context refuses archived pools and archived accounts,
/// takes a single id, and carries only TODAY's balance — all correct for a
/// command, all wrong for a page that has to stay drillable after the pool is
/// wound down.
/// </para>
/// <para>
/// Fixed round-trip count regardless of how many pools are in play: accounts,
/// participants, events, transactions. No N+1.
/// </para>
/// </summary>
internal sealed class PoolReadContext
{
    private readonly Dictionary<Guid, Account> _accountsById;
    private readonly Dictionary<Guid, List<PoolParticipant>> _participantsByPoolId;
    private readonly Dictionary<Guid, List<PoolUnitEvent>> _eventsByPoolId;
    private readonly AccountBalanceLedger _ledger;

    private PoolReadContext(
        Dictionary<Guid, Account> accountsById,
        Dictionary<Guid, List<PoolParticipant>> participantsByPoolId,
        Dictionary<Guid, List<PoolUnitEvent>> eventsByPoolId,
        AccountBalanceLedger ledger)
    {
        _accountsById = accountsById;
        _participantsByPoolId = participantsByPoolId;
        _eventsByPoolId = eventsByPoolId;
        _ledger = ledger;
    }

    public static async Task<PoolReadContext> LoadAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Pool> pools,
        CancellationToken cancellationToken)
    {
        if (pools.Count == 0)
        {
            return new PoolReadContext([], [], [], await AccountBalanceLedger.LoadAsync(db, cancellationToken));
        }

        Guid[] poolIds = [.. pools.Select(p => p.Id)];
        Guid[] accountIds = [.. pools.Select(p => p.AccountId).Distinct()];

        // IgnoreQueryFilters + no archive predicate: an archived account's name
        // must still label its pool, the same rule GetLoanDetail applies to the
        // accounts behind historical movements.
        List<Account> accounts = await db.Accounts
            .IgnoreQueryFilters()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        // ARCHIVED PARTICIPANTS INCLUDED, deliberately. The model rests on
        // Σ participantUnits == totalUnits; filtering the roster hands an exited
        // friend's residual units to the owner with no event to explain it.
        List<PoolParticipant> participants = await db.PoolParticipants
            .Where(p => poolIds.Contains(p.PoolId))
            .ToListAsync(cancellationToken);

        List<PoolUnitEvent> events = await db.PoolUnitEvents
            .Where(e => poolIds.Contains(e.PoolId))
            .ToListAsync(cancellationToken);

        // The shared "opening anchor + Σ income − Σ expense" seam the net-worth
        // surfaces use. Reusing it (rather than re-deriving the sum) is what
        // keeps the NAV this page prints and the balance the dashboard shows
        // from ever disagreeing.
        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(db, cancellationToken);

        return new PoolReadContext(
            accounts.ToDictionary(a => a.Id),
            participants.GroupBy(p => p.PoolId).ToDictionary(g => g.Key, g => g.ToList()),
            events.GroupBy(e => e.PoolId).ToDictionary(g => g.Key, g => g.ToList()),
            ledger);
    }

    /// <summary>
    /// The pool's account. <c>null</c> only if the row was deleted out of band —
    /// the FK is <c>RESTRICT</c>, so this cannot happen through the app.
    /// </summary>
    public Account? AccountFor(Pool pool) => _accountsById.GetValueOrDefault(pool.AccountId);

    public IReadOnlyList<PoolParticipant> ParticipantsFor(Guid poolId) =>
        _participantsByPoolId.GetValueOrDefault(poolId, []);

    public IReadOnlyList<PoolUnitEvent> EventsFor(Guid poolId) =>
        _eventsByPoolId.GetValueOrDefault(poolId, []);

    /// <summary>The account's derived balance in its own currency, at any date.</summary>
    public decimal BalanceAsOf(Account account, DateOnly asOf) => _ledger.NativeBalanceAsOf(account, asOf);

    /// <summary>
    /// The pool folded at <paramref name="asOf"/>, priced against the account's
    /// derived balance on that same date. <c>null</c> when the account is
    /// missing.
    /// </summary>
    public PoolSnapshot? SnapshotFor(Pool pool, DateOnly asOf)
    {
        Account? account = AccountFor(pool);
        if (account is null)
        {
            return null;
        }

        return PoolUnitRegister
            .Create(ParticipantsFor(pool.Id), EventsFor(pool.Id))
            .SnapshotAsOf(asOf, BalanceAsOf(account, asOf));
    }
}
