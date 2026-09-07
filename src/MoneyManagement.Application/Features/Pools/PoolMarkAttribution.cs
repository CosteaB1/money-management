using System.Collections.ObjectModel;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Works out, for each re-pricing mark on a pooled account, <b>how much of it
/// was the owner's</b> — the share they held at the instant the mark was
/// struck, not at the end of the day it is dated on.
/// <para>
/// A mark re-prices the whole pool, so it splits pro-rata across whoever held
/// units when it was written. Every pool command writes its mark FIRST and
/// prices units against the marked balance SECOND, inside one
/// <c>SaveChangesAsync</c> (see <see cref="PoolMark"/>), so the fraction that
/// applies is the one in force <i>before</i> the event the mark belongs to —
/// "pre-money".
/// </para>
/// <para>
/// <b>Why the previous rule (<c>OwnedFractionAsOf(date − 1)</c>) was not
/// enough.</b> The ownership curve carries one END-OF-DAY point per date, so
/// "the day before" is the state before that day's events — correct whenever a
/// date holds at most one capital event, which is the normal case and what
/// POOLED-CAPITAL.md §6 assumes. Put a capital event and a LATER mark on one
/// calendar day and it breaks: the mark is attributed at the PRE-event fraction
/// and the owner is over-credited. Observed live on 2026-09-07 — a pool
/// bootstrapped and subscribed on one day, then closed the same evening, gave
/// the +208.00 USD close mark whole to the owner instead of 1092/2092 of it, a
/// 99.43 USD over-statement of Net P&amp;L. (Net worth was never affected: it
/// reads the end-of-day fraction, which is right for a whole-day balance.)
/// </para>
/// <para>
/// <b>The pairing, and why it survives same-millisecond writes.</b> A mark and
/// the unit event it prices are written in ONE save, and .NET's
/// <c>Guid.CreateVersion7</c> has no intra-millisecond counter — ordering them
/// by id would be a coin toss. So they are not ordered by id at all. Instead the
/// pairing is read out of recorded DATA: every non-Seed event stores
/// <c>PoolValuePreMoney</c> = the balance <i>including</i> its own mark, so
/// re-deriving that pre-money from the account's rows leaves a gap exactly equal
/// to the marks written since the previous event, and
/// <see cref="PoolMarkLedger.Claim"/> consumes them as a prefix.
/// <see cref="PoolPreMoneyReplay"/> already does precisely this for the
/// reconciliation tripwire; this type reads its output for a different purpose
/// rather than forking it.
/// </para>
/// <list type="bullet">
/// <item>
/// A mark <b>claimed by</b> event <c>E</c> was struck immediately before
/// <c>E</c>, so it takes
/// <see cref="PoolOwnershipTimeline.FractionBefore"/> at <c>E</c> — after every
/// event recorded earlier, including earlier ones on the same day.
/// </item>
/// <item>
/// A mark <b>claimed by nobody</b> — a hand-typed <c>AdjustBalance</c> snapshot,
/// which the guard table still permits on a pooled account when dated today, or
/// any mark written after the day's last event — was struck after everything
/// that day, so it takes
/// <see cref="PoolOwnershipTimeline.FractionAtEndOf"/>. On a day with no capital
/// event at all that is identical to the old <c>date − 1</c> answer, which is why
/// nothing outside the same-day case moves.
/// </item>
/// </list>
/// <para>
/// <b>Known residue, stated rather than hidden.</b> Two situations still have no
/// evidence in the data to resolve them, and both fall back to the end-of-day
/// reading: a manual snapshot typed BEFORE a same-day <c>CreatePool</c> (the
/// seed is skipped by the replay, so nothing can claim the mark) and a mark
/// written before a distribution SETTLES on that same day (settlement mutates an
/// existing event and stamps only a date, so there is no write-order key for it).
/// Both need two commands on one calendar day AND a non-trivial fraction change
/// inside that day to matter at all.
/// </para>
/// </summary>
internal static class PoolMarkAttribution
{
    /// <param name="events">
    /// The pool's whole ledger — unordered; this sorts. Empty when the account
    /// has no pool, which is the signal to leave every mark alone.
    /// </param>
    /// <param name="ownerParticipantIds">
    /// The participant rows flagged <c>IsOwner</c>. A set rather than a single id
    /// because the caller already has the joined rows and the partial unique
    /// index — not this type — is what guarantees there is exactly one.
    /// </param>
    /// <param name="marks">
    /// Every <c>IsAdjustment</c> row on the pool's account, signed. Includes rows
    /// dated before the pool existed: those simply resolve to 1.0, which is what
    /// they must be.
    /// </param>
    /// <param name="balanceAsOf">
    /// The account's derived balance at an arbitrary date — the same
    /// "opening anchor + Σ income − Σ expense" seam
    /// <c>AccountBalanceLedger.NativeBalanceAsOf</c> implements. A delegate so
    /// this stays pure and the caller can fold rows it has already loaded
    /// instead of issuing another query.
    /// </param>
    /// <param name="asOf">Today, matching the reconciliation's own cutoff.</param>
    /// <returns>
    /// Transaction id → the owner's share of that mark, one entry per mark.
    /// <b>Empty when there is no pool</b>, so a caller can treat "not in here" as
    /// "nothing pool-shaped applies" and keep its pre-existing behaviour
    /// bit-for-bit.
    /// </returns>
    public static IReadOnlyDictionary<Guid, decimal> Build(
        IReadOnlyList<PoolUnitEvent> events,
        IReadOnlySet<Guid> ownerParticipantIds,
        IReadOnlyList<PoolMarkRow> marks,
        Func<DateOnly, decimal> balanceAsOf,
        DateOnly asOf)
    {
        if (events.Count == 0 || marks.Count == 0)
        {
            return ReadOnlyDictionary<Guid, decimal>.Empty;
        }

        // The SAME fold the net-worth seam consumes, read at movement
        // granularity. Deriving the fraction here from anything else - a second
        // units sum, a NAV, a balance ratio - is how the Performance card and
        // the dashboard would start disagreeing about who owns the account.
        PoolOwnershipTimeline timeline = PoolUnitRegister.OwnershipTimeline(
            events.Select(e => new PoolUnitMovement(
                e.OccurredOn,
                e.Id,
                ownerParticipantIds.Contains(e.ParticipantId),
                e.Kind,
                e.Units,
                e.SettledOn)));

        PoolUnitEvent[] ordered = [.. events.OrderBy(e => e.OccurredOn).ThenBy(e => e.Id)];

        var fractions = new Dictionary<Guid, decimal>(marks.Count);

        foreach (PoolPricedEvent priced in PoolPreMoneyReplay.Run(
            ordered,
            PoolMarkLedger.Create(marks),
            balanceAsOf,
            asOf))
        {
            foreach (PoolMarkRow claimed in priced.ClaimedMarks)
            {
                // OccurredOn, not the effective date: this asks "what had been
                // recorded when the mark was written", and a distribution is
                // recorded on the day it closes even though its units retire on
                // the day it is paid.
                fractions[claimed.TransactionId] =
                    timeline.FractionBefore(priced.Event.OccurredOn, priced.Event.Id);
            }
        }

        // Whatever nothing claimed was written after the day's last recorded
        // event - or on a day that had none at all, which is every mark on an
        // ordinary month and every mark predating the pool.
        foreach (PoolMarkRow mark in marks)
        {
            if (!fractions.ContainsKey(mark.TransactionId))
            {
                fractions[mark.TransactionId] = timeline.FractionAtEndOf(mark.Date);
            }
        }

        return fractions;
    }
}
