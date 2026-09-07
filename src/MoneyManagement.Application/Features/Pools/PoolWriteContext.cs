using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Everything a pool write command needs, loaded once: the pool, its account,
/// the full roster, the full unit ledger, and the account's derived balance
/// today.
/// <para>
/// Exists so the five write handlers cannot drift apart on which rows they
/// consider. In particular the roster is loaded WITHOUT an archived filter —
/// <c>Σ participantUnits == totalUnits</c> is the model's core identity and a
/// filtered roster breaks it silently.
/// </para>
/// </summary>
internal sealed class PoolWriteContext
{
    private PoolWriteContext(
        Pool pool,
        Account account,
        IReadOnlyList<PoolParticipant> participants,
        IReadOnlyList<PoolUnitEvent> events,
        decimal derivedBalance,
        DateOnly today)
    {
        Pool = pool;
        Account = account;
        Participants = participants;
        Events = events;
        DerivedBalance = derivedBalance;
        Today = today;
        Register = PoolUnitRegister.Create(participants, events);
    }

    public Pool Pool { get; }

    public Account Account { get; }

    /// <summary>Every participant, archived included. See the type remarks.</summary>
    public IReadOnlyList<PoolParticipant> Participants { get; }

    public IReadOnlyList<PoolUnitEvent> Events { get; }

    public PoolUnitRegister Register { get; }

    /// <summary>
    /// The account's balance today, BEFORE this command writes anything —
    /// i.e. what the app currently believes. The mark's delta is measured
    /// against it, and the <c>cash_looks_like_a_balance</c> guard compares
    /// against it.
    /// </summary>
    public decimal DerivedBalance { get; }

    public DateOnly Today { get; }

    public string Currency => Pool.Currency;

    /// <summary>
    /// Loads the context for a NON-ARCHIVED pool. An archived pool (or an
    /// archived account) resolves to <c>NotFound</c>: the write paths are for
    /// live pools only, and archive/unarchive load their own rows with
    /// <c>IgnoreQueryFilters</c>.
    /// </summary>
    public static async Task<Result<PoolWriteContext>> LoadAsync(
        IApplicationDbContext db,
        Guid poolId,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        // The is_archived query filter hides archived pools; the explicit
        // predicate is defense-in-depth for unit tests that bypass model
        // configuration (same convention as the loans slice).
        Pool? pool = await db.Pools
            .FirstOrDefaultAsync(p => p.Id == poolId && !p.IsArchived, cancellationToken);

        if (pool is null)
        {
            return Result.Failure<PoolWriteContext>(PoolErrors.NotFound(poolId));
        }

        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == pool.AccountId && !a.IsArchived, cancellationToken);

        if (account is null)
        {
            return Result.Failure<PoolWriteContext>(AccountErrors.NotFound(pool.AccountId));
        }

        List<PoolParticipant> participants = await db.PoolParticipants
            .Where(p => p.PoolId == poolId)
            .ToListAsync(cancellationToken);

        // Integrity tripwires. Both conditions are prevented by the schema (the
        // owner row is created atomically with the pool, can never be archived,
        // and is pinned by a filtered unique index), so reaching either means a
        // restore or an out-of-band write has broken the model. Fail with a
        // named error rather than fold a broken roster into an ownership
        // fraction — a missing owner would hand the whole account to the
        // friends, a duplicate one would double-count the user.
        int owners = participants.Count(p => p.IsOwner);
        if (owners == 0)
        {
            return Result.Failure<PoolWriteContext>(PoolErrors.OwnerRequired);
        }

        if (owners > 1)
        {
            return Result.Failure<PoolWriteContext>(PoolErrors.OwnerAlreadyExists);
        }

        List<PoolUnitEvent> events = await db.PoolUnitEvents
            .Where(e => e.PoolId == poolId)
            .ToListAsync(cancellationToken);

        // Likewise: a second seed would restate the pool's entire basis, so the
        // ledger is refused rather than priced.
        if (events.Count(e => e.Kind == PoolUnitEventKind.Seed) > 1)
        {
            return Result.Failure<PoolWriteContext>(PoolErrors.SeedAlreadyExists);
        }

        // AccountBalanceLedger is the shared "opening anchor + Σ income − Σ expense"
        // seam the net-worth surfaces use. Reusing it (rather than re-deriving the
        // sum here) is what keeps the NAV the pool strikes and the balance the
        // dashboard shows from ever disagreeing.
        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(db, cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        decimal derivedBalance = ledger.NativeBalanceAsOf(account, today);

        return new PoolWriteContext(pool, account, participants, events, derivedBalance, today);
    }

    /// <summary>
    /// Resolves a participant of THIS pool, rejecting archived rows. A valid id
    /// belonging to another pool is treated as not-found rather than leaking
    /// cross-pool access (the loans slice's convention).
    /// </summary>
    public Result<PoolParticipant> RequireActiveParticipant(Guid participantId)
    {
        PoolParticipant? participant = Participants.FirstOrDefault(p => p.Id == participantId);

        if (participant is null)
        {
            return Result.Failure<PoolParticipant>(PoolErrors.ParticipantNotFound(participantId));
        }

        return participant.IsArchived
            ? Result.Failure<PoolParticipant>(PoolErrors.ParticipantIsArchived)
            : Result.Success(participant);
    }

    /// <summary>
    /// The state the pool is in right now, priced against
    /// <paramref name="markedBalance"/> — the value the caller has just marked
    /// the account to. Pass <see cref="DerivedBalance"/> to price against the
    /// unmarked balance.
    /// </summary>
    public PoolSnapshot SnapshotToday(decimal markedBalance) =>
        Register.SnapshotAsOf(Today, markedBalance);

    /// <summary>
    /// Blocks the demonstrated data-entry slip: a cash amount that equals a
    /// BALANCE (either the one the app derived or the one the user just typed
    /// as the pool's total) to the cent. Two soft-deleted rows on the real
    /// account are exactly this mistake.
    /// </summary>
    /// <param name="allowFullWindDown">
    /// The one sanctioned exception, and only <c>RecordRedemption</c> passes it.
    /// Redeeming the LAST of a pool is by definition redeeming the whole pool
    /// value, so the guard is true and unhelpful at exactly that moment: without
    /// an opt-out, a pool can be driven towards zero units by repeated partial
    /// redemptions but never actually reach it, and "I moved everything to Bybit
    /// and I'm done" has no path through the API.
    /// <para>
    /// Deliberately NOT self-detecting. Inferring "this looks like a wind-down"
    /// from the numbers would re-admit the very typo the guard exists for — the
    /// slip and the wind-down are numerically identical. The caller has to say
    /// so, and <c>RecordRedemptionCommandHandler</c> then checks the claim
    /// against the units it is about to retire.
    /// </para>
    /// </param>
    public Result GuardCashIsNotABalance(decimal cash, decimal markedBalance, bool allowFullWindDown = false)
    {
        if (allowFullWindDown)
        {
            return Result.Success();
        }

        if (cash == PoolUnitRegister.RoundMoney(DerivedBalance)
            || cash == PoolUnitRegister.RoundMoney(markedBalance))
        {
            return Result.Failure(PoolErrors.CashLooksLikeABalance);
        }

        return Result.Success();
    }
}
