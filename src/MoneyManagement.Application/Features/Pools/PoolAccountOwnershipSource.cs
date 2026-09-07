using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.NetWorth;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Projects pools into <see cref="AccountOwnership"/> curves, so net worth
/// counts only the user's SHARE of a pooled account instead of its whole
/// balance. The first — and so far only — producer for the ownership seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE flat query, and the fraction is a ratio of UNIT COUNTS ONLY.</b> The
/// projection reaches pools ⋈ participants ⋈ unit events and reads six
/// columns — unit counts and dates, nothing else. It deliberately does not
/// touch <c>Transactions</c>, account balances, <c>NavPerUnit</c> or
/// <see cref="Abstractions.FxRates.IFxConverter"/> — and must not be
/// "improved" to. Two things depend on that:
/// </para>
/// <list type="bullet">
/// <item>
/// The net-worth trend evaluates up to 24 as-of dates. A fraction that needed a
/// balance or a rate would turn this one round-trip into 24 × accounts × (a
/// balance query + an FX lookup); as pure unit counts it is loaded once and
/// sliced in memory by <c>AccountOwnershipLedger</c>.
/// </item>
/// <item>
/// A NAV later found to be wrong misprices nothing retroactively. Units were
/// minted at whatever price applied on the day; the SHARE they represent is
/// arithmetic over counts, so correcting a bad mark changes what the pool is
/// worth without changing who owns what.
/// </item>
/// </list>
/// <para>
/// <b>A distribution retires its units on the day the CASH LEFT
/// (<c>SettledOn</c>), not on the day the month closed.</b> Close and pay are
/// two separate steps: the close redeems units and records the amount owed,
/// and the USDT physically goes out days later. This source hands the
/// dashboard a fraction it multiplies into the account's RAW balance, and in
/// that window the balance still holds the friends' payout. Retire the units
/// at the close and the owner is credited with
/// <c>ownerFraction × unpaidDistributionCash</c> of money that is already
/// somebody else's — on §6's figures, USD 822.29 against a true stake of
/// 778.12 — and a month-end close welds that error onto that trend point
/// permanently, because the payment row is dated after the as-of cutoff.
/// Dating the retirement at settle makes paying a distribution move net worth
/// by exactly zero, which is the identity the design is built on: paying
/// somebody what you owe them makes you no poorer. A closed-but-unpaid
/// distribution therefore contributes NOTHING to the curve.
/// </para>
/// <para>
/// <b><see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}(IQueryable{TEntity})"/>
/// is mandatory here</b>, for the same reason <c>LoanExternalClaimSource</c>
/// needs it and as flagged in <c>PoolConfiguration</c>: <c>Pool</c> carries
/// <c>HasQueryFilter(p =&gt; !p.IsArchived)</c>, and an archived pool's history
/// still shapes PAST dates on the net-worth trend. Dropping it would silently
/// rewrite months that really did have somebody else's money in them. (Archiving
/// requires zero outside units, so the curve's final point is 1.0 anyway — the
/// filter would erase the middle of the story, not the end of it.)
/// </para>
/// <para>
/// The unit arithmetic itself lives in <see cref="PoolUnitRegister.OwnershipCurve"/>,
/// shared with the read queries so the dashboard and the pool page cannot
/// disagree about who owns what.
/// </para>
/// </remarks>
internal sealed class PoolAccountOwnershipSource(IApplicationDbContext db) : IAccountOwnershipSource
{
    public async Task<IReadOnlyList<AccountOwnership>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        // Pools ⋈ unit events ⋈ participants, projected to the six columns the
        // fold needs — unit counts and dates only, no balance and no rate.
        // Joined explicitly rather than through navigations because the pool
        // aggregates are configured with shadow FKs and no navigation properties
        // (see PoolUnitEventConfiguration).
        //
        // Ordering is done in MEMORY below, never here: ordering inside the
        // query would be translated by Postgres and merely mimicked by the unit
        // tests' in-memory provider, which is exactly how an untranslatable
        // OrderBy shipped from this repo once before.
        var rows = await db.Pools
            .IgnoreQueryFilters()
            .Join(
                db.PoolUnitEvents,
                pool => pool.Id,
                unitEvent => unitEvent.PoolId,
                (pool, unitEvent) => new { pool.AccountId, Event = unitEvent })
            .Join(
                db.PoolParticipants,
                row => row.Event.ParticipantId,
                participant => participant.Id,
                (row, participant) => new
                {
                    row.AccountId,
                    row.Event.OccurredOn,
                    EventId = row.Event.Id,
                    participant.IsOwner,
                    row.Event.Kind,
                    row.Event.Units,
                    // A DATE, not a balance: the no-FX/no-transaction contract
                    // above is intact. It is what dates a distribution's
                    // retirement at the day its cash actually left.
                    row.Event.SettledOn,
                })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // One pool per account, enforced forever by the UNFILTERED unique index
        // ix_pools_account_id, so grouping by account is the same partition as
        // grouping by pool — and an account can never accumulate two overlapping
        // curves that would have to be multiplied together.
        var ownerships = new List<AccountOwnership>();

        foreach (var group in rows.GroupBy(r => r.AccountId))
        {
            IReadOnlyList<OwnedFractionPoint> points = PoolUnitRegister.OwnershipCurve(
                group.Select(r => new PoolUnitMovement(
                    r.OccurredOn,
                    r.EventId,
                    r.IsOwner,
                    r.Kind,
                    r.Units,
                    r.SettledOn)));

            if (points.Count == 0)
            {
                continue;
            }

            ownerships.Add(new AccountOwnership(group.Key, points));
        }

        return ownerships;
    }
}
