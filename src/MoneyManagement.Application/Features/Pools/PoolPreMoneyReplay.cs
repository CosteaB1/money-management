using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Re-derives every unit event's <c>PoolValuePreMoney</c> from the account's own
/// rows and reports which re-pricing marks each event was struck against.
/// <para>
/// <b>Extracted from <see cref="PoolReconciliation"/>, and shared with
/// <see cref="PoolMarkAttribution"/> on purpose.</b> The two callers ask
/// different questions of the same walk — the tripwire wants the events that do
/// NOT reproduce, the account-detail card wants the mark→event pairing that
/// falls out of the ones that DO — and a second copy of this arithmetic would be
/// a guaranteed future divergence between "the pool page says the ledger is
/// clean" and "the Performance card split the marks that way".
/// </para>
/// <para>
/// The write path prices every event as <c>markedBalance − unpaidDistributions</c>
/// at the moment it is recorded, so the replay undoes exactly that: take the
/// account's balance on the event's date, remove the cash of every event that
/// had landed by then but did not yet exist when this one was priced, rewind the
/// day's re-pricing back to the marks this event was actually struck against,
/// then subtract the distributions that were still owed.
/// </para>
/// <para>
/// <b>A close is replayed as a unit, not line by line.</b> Every line of one
/// close is struck against a single pre-money, before any of them exists, so
/// each line has to undo its SIBLINGS' cash as well as its own — otherwise a
/// two-line close paid out the same day reports itself as drifted. Sibling lines
/// are recognised by their shared <c>PoolValuePreMoney</c>
/// (<see cref="IsSameCloseLine"/>), never by their date alone, so two separate
/// closes landing on one day do not cancel each other into a false clean bill.
/// </para>
/// <para>
/// <b>Re-pricing marks are folded FORWARD, never subtracted backwards.</b>
/// <c>balanceAsOf</c> answers END-OF-DAY, so an <c>IsAdjustment</c> row written
/// later on the event's own day is already inside it — and there is no ordering
/// key in the row shape that says so. The replay therefore strips every
/// still-unclaimed mark dated that day, leaving the balance as it stood before
/// the day's re-pricing, and then lets the event CLAIM the marks its own
/// recorded pre-money implies (<see cref="PoolMarkLedger.Claim"/>). Whatever it
/// does not claim was written after it, and is correctly left for a later event
/// — or for nobody.
/// </para>
/// <para>
/// Without that, "subscribe in the morning, snapshot the exchange in the
/// evening" — an ordinary sequence with no guard bypassed, since
/// <c>AdjustBalance</c> deliberately still allows a mark dated today on a pooled
/// account — reported a drift on every event already priced that day, exactly
/// equal to the mark. A tripwire that cries wolf on the one check the design
/// tells the user to trust gets ignored, and then the finding that matters (a
/// third party's stake mispriced) goes unread.
/// </para>
/// <para>
/// <b>Seeds are skipped</b> — their pre-money is zero by definition (the pool
/// holds nothing before it strikes), so there is nothing to reproduce and
/// nothing to claim.
/// </para>
/// <para>
/// <b>Back-dated events are skipped</b> — <c>CreatePool</c>'s backfill list is
/// the one place in the slice where an event may be dated in the past, and its
/// pre-money comes from the EXCHANGE's history, not from the app's ledger. The
/// app never claimed it could reproduce those numbers: a market move between
/// inception and the arrival has no row anywhere, so checking them would fail
/// permanently on every pool set up from history. An event is back-dated when it
/// occurred before the day it was recorded
/// (<see cref="MoneyManagement.SharedKernel.Entity.CreatedAt"/>, stamped by the
/// audit interceptor); an unstamped row — only reachable in unit tests — is
/// treated as same-day and therefore checked.
/// </para>
/// </summary>
internal static class PoolPreMoneyReplay
{
    /// <summary>
    /// Half a cent. Everything compared here is money at <c>numeric(18,2)</c>,
    /// so a real disagreement is at least one cent and anything smaller is
    /// rounding.
    /// </summary>
    private const decimal MoneyTolerance = PoolUnitEvent.CashReconciliationTolerance;

    /// <param name="ordered">
    /// The pool's whole ledger, ordered by <c>(OccurredOn, Id)</c>. Ids are
    /// UUIDv7, so that is the order the events were recorded in — see
    /// <see cref="PoolMarkLedger"/> for what that ordering can and cannot be
    /// trusted to decide.
    /// </param>
    /// <param name="marks">
    /// The account's re-pricing marks, CONSUMED as the walk proceeds: each event
    /// removes the ones it was struck against, so a later event on the same day
    /// sees them as part of the balance it was priced from instead of claiming
    /// them a second time. Pass a freshly built ledger per replay.
    /// </param>
    /// <param name="balanceAsOf">
    /// The account's derived balance at an arbitrary date — normally
    /// <c>AccountBalanceLedger.NativeBalanceAsOf</c>, bound to the pool's
    /// account. A delegate so the same seam the dashboard uses is the one
    /// replayed here.
    /// </param>
    /// <param name="asOf">Today. Rows dated after it are the caller's problem, not this walk's.</param>
    /// <returns>
    /// One entry per event the replay actually priced, in walk order. Skipped
    /// events (seeds, future, back-dated) are absent rather than reported as
    /// reproducing, so a caller can never mistake "not checked" for "checked and
    /// fine".
    /// </returns>
    public static IReadOnlyList<PoolPricedEvent> Run(
        IReadOnlyList<PoolUnitEvent> ordered,
        PoolMarkLedger marks,
        Func<DateOnly, decimal> balanceAsOf,
        DateOnly asOf)
    {
        var priced = new List<PoolPricedEvent>(ordered.Count);

        for (int i = 0; i < ordered.Count; i++)
        {
            PoolUnitEvent unitEvent = ordered[i];

            if (unitEvent.Kind == PoolUnitEventKind.Seed
                || unitEvent.OccurredOn > asOf
                || WasBackdated(unitEvent))
            {
                continue;
            }

            DateOnly on = unitEvent.OccurredOn;
            decimal balance = balanceAsOf(on);

            // Undo the cash of every event that had already landed on or before
            // this date but did NOT exist when this one was priced: this event
            // itself, everything ordered after it (two subscriptions recorded
            // the same day, say), and - for a distribution - the EARLIER lines
            // of its own close, which share a single pre-money struck before any
            // of them existed. Miss those and a two-line close paid out the same
            // day reports itself as drifted, because the first line's cash was
            // removed from the balance and never added back.
            //
            // Earlier same-day subscriptions and redemptions stay put: they were
            // priced, and marked, before the close.
            for (int j = 0; j < ordered.Count; j++)
            {
                PoolUnitEvent other = ordered[j];

                if (j < i && !IsSameCloseLine(unitEvent, other))
                {
                    continue;
                }

                if (other.SettledOn is DateOnly settled && settled <= on)
                {
                    balance -= SignedCash(other);
                }
            }

            // Rewind the day's re-pricing. Every mark still unclaimed on this
            // date comes back out, so what is left is the account as it stood
            // BEFORE the day was re-priced at all; the claim below puts back the
            // ones this event was actually struck against. Marks that earlier
            // events on this day already claimed are gone from the ledger and so
            // stay in the balance — correctly, they were part of this event's
            // base too.
            foreach (PoolMarkRow mark in marks.UnclaimedOn(on))
            {
                balance -= mark.Signed;
            }

            // Distributions that had closed but not yet been paid: their cash was
            // still sitting in the account, and the write path subtracted it.
            decimal unpaid = 0m;
            for (int j = 0; j < i; j++)
            {
                PoolUnitEvent earlier = ordered[j];
                if (earlier.Kind != PoolUnitEventKind.Distribution)
                {
                    continue;
                }

                // Every line of ONE close shares a single pre-money, struck
                // before any of them existed. Sibling lines therefore must not
                // be subtracted when replaying each other.
                //
                // Scoped to the CLOSE, not to the calendar day: two separate
                // closes on one date were each really outstanding when the other
                // was priced, and exempting by date alone had them cancel each
                // other out into a false clean bill.
                if (IsSameCloseLine(unitEvent, earlier))
                {
                    continue;
                }

                if (earlier.SettledOn is not DateOnly settled || settled > on)
                {
                    unpaid += earlier.Cash?.Amount ?? 0m;
                }
            }

            decimal derived = PoolUnitRegister.RoundMoney(balance - unpaid);
            decimal drift = unitEvent.PoolValuePreMoney - derived;

            // Priced against an account the app already agreed with: PoolMark
            // writes no row when the typed value equals the derived one, so there
            // is nothing to claim and nothing to report.
            if (Math.Abs(drift) <= MoneyTolerance)
            {
                priced.Add(new PoolPricedEvent(unitEvent, derived, drift, [], Reproduces: true));
                continue;
            }

            // Otherwise the gap IS the re-pricing this event was struck after. If
            // the day's marks account for it, the ledger reproduces and those
            // marks are this event's; if they do not, a row some price depended
            // on has been moved, edited or deleted, and that is the finding.
            IReadOnlyList<PoolMarkRow>? claimed = marks.Claim(on, drift);

            priced.Add(new PoolPricedEvent(
                unitEvent,
                derived,
                drift,
                claimed ?? [],
                Reproduces: claimed is not null));
        }

        return priced;
    }

    /// <summary>
    /// Whether <paramref name="other"/> is a line of the same CLOSE as
    /// <paramref name="unitEvent"/>: both distributions, dated the same day,
    /// recorded against the same pre-money.
    /// <para>
    /// There is no close id in the schema and none is needed. Every line of one
    /// close is written in a single <c>SaveChangesAsync</c> from one
    /// <c>PoolValuePreMoney</c>, so the recorded pre-money IS the close's
    /// identity — and two closes on the same day cannot share one, because the
    /// first one's payout leaves the second one less to price against.
    /// </para>
    /// </summary>
    private static bool IsSameCloseLine(PoolUnitEvent unitEvent, PoolUnitEvent other) =>
        unitEvent.Kind == PoolUnitEventKind.Distribution
        && other.Kind == PoolUnitEventKind.Distribution
        && other.OccurredOn == unitEvent.OccurredOn
        && Math.Abs(other.PoolValuePreMoney - unitEvent.PoolValuePreMoney) <= MoneyTolerance;

    private static bool WasBackdated(PoolUnitEvent unitEvent) =>
        unitEvent.CreatedAt != default && unitEvent.OccurredOn < DateOnly.FromDateTime(unitEvent.CreatedAt);

    /// <summary>The event's effect on the account's balance, signed. Zero for the no-cash kinds.</summary>
    private static decimal SignedCash(PoolUnitEvent unitEvent)
    {
        decimal cash = unitEvent.Cash?.Amount ?? 0m;

        return unitEvent.Kind switch
        {
            PoolUnitEventKind.Subscription => cash,
            PoolUnitEventKind.Redemption or PoolUnitEventKind.Distribution => -cash,
            _ => 0m,
        };
    }
}

/// <summary>
/// One event as the replay saw it.
/// </summary>
/// <param name="Event">The event replayed.</param>
/// <param name="DerivedPreMoney">What the account's rows say its pre-money was.</param>
/// <param name="Drift">
/// <c>Recorded − Derived</c>. Non-zero does NOT mean broken: it is the
/// re-pricing the event was struck after, and <paramref name="ClaimedMarks"/>
/// says whether the account's marks account for it.
/// </param>
/// <param name="ClaimedMarks">
/// The marks this event was struck immediately after, oldest first — empty when
/// the event needed none, and empty when the gap could not be explained.
/// <b>These are the marks whose owner-share is the fraction in force BEFORE this
/// event</b>, which is the whole reason the account-detail card shares this
/// walk.
/// </param>
/// <param name="Reproduces">
/// Whether the recorded pre-money is reproducible from the account: either the
/// gap was zero, or the day's marks closed it exactly.
/// </param>
internal readonly record struct PoolPricedEvent(
    PoolUnitEvent Event,
    decimal DerivedPreMoney,
    decimal Drift,
    IReadOnlyList<PoolMarkRow> ClaimedMarks,
    bool Reproduces);

/// <summary>
/// One re-pricing mark on a pool's account, signed, carrying the id of the row
/// it came from so a caller can map the claim back to the transaction.
/// </summary>
/// <param name="TransactionId">The <c>IsAdjustment</c> row.</param>
/// <param name="Date">The day it is dated on.</param>
/// <param name="Signed">
/// Positive for an Income mark, negative for an Expense one — the amount it
/// moved the account's balance by.
/// </param>
internal readonly record struct PoolMarkRow(Guid TransactionId, DateOnly Date, decimal Signed);

/// <summary>
/// Every re-pricing mark on a pool's account, grouped by the day it is dated on,
/// ordered WITHIN that day the way it was written, and consumed as events claim
/// them.
/// <para>
/// Ids are UUIDv7, so ordering by id is ordering by creation time — the same
/// stand-in for <c>CreatedAt</c> that <c>PoolReconciliation</c>'s money check and
/// the read slice's event list already use. <b>It decides only the order of rows
/// written by SEPARATE commands</b>, which in a single-user app are separated by
/// human interaction time. It is never used to compare a mark against the unit
/// event it prices: those are minted inside one handler and usually inside one
/// millisecond, and .NET's <c>Guid.CreateVersion7</c> carries no intra-millisecond
/// counter, so that comparison would be a coin toss. The structural pairing
/// below is what replaces it.
/// </para>
/// </summary>
internal sealed class PoolMarkLedger
{
    private const decimal MoneyTolerance = PoolUnitEvent.CashReconciliationTolerance;

    private static readonly PoolMarkRow[] NoMarks = [];

    private readonly Dictionary<DateOnly, List<PoolMarkRow>> _unclaimedByDate;

    private PoolMarkLedger(Dictionary<DateOnly, List<PoolMarkRow>> unclaimedByDate) =>
        _unclaimedByDate = unclaimedByDate;

    /// <summary>
    /// Picks the marks out of a pool account's rows. <c>IsAdjustment</c> is the
    /// whole test: the re-pricing mark is deliberately shaped exactly like a
    /// hand-typed snapshot (see <see cref="PoolMark"/>), and for this walk the
    /// two really are interchangeable — both re-price the pool pro-rata and
    /// neither carries a unit event.
    /// </summary>
    public static PoolMarkLedger FromAccountRows(IEnumerable<PoolAccountRow> accountRows) =>
        Create(accountRows
            .Where(r => r.IsAdjustment)
            .Select(r => new PoolMarkRow(
                r.Id,
                r.Date,
                r.Direction == TransactionDirection.Income ? r.Amount : -r.Amount)));

    public static PoolMarkLedger Create(IEnumerable<PoolMarkRow> marks)
    {
        var byDate = new Dictionary<DateOnly, List<PoolMarkRow>>();

        foreach (PoolMarkRow mark in marks.OrderBy(m => m.TransactionId))
        {
            if (!byDate.TryGetValue(mark.Date, out List<PoolMarkRow>? onThatDay))
            {
                onThatDay = [];
                byDate[mark.Date] = onThatDay;
            }

            onThatDay.Add(mark);
        }

        return new PoolMarkLedger(byDate);
    }

    /// <summary>
    /// The marks dated <paramref name="date"/> that no event has claimed yet, in
    /// write order. Empty — never null — so a caller can strip them
    /// unconditionally.
    /// </summary>
    public IReadOnlyList<PoolMarkRow> UnclaimedOn(DateOnly date) =>
        _unclaimedByDate.GetValueOrDefault(date) ?? (IReadOnlyList<PoolMarkRow>)NoMarks;

    /// <summary>
    /// Consumes the marks that explain <paramref name="gap"/> — the difference
    /// between what an event recorded as its pre-money and what the day's
    /// un-re-priced balance comes to — and returns them, oldest first, or
    /// <c>null</c> when they do not add up.
    /// <para>
    /// A <b>PREFIX</b> is taken, never an arbitrary subset. The list is in the
    /// order the marks were written and everything inside an event's pre-money
    /// was written before it, so the marks it was priced against are always the
    /// ones at the front of what is left. Two snapshots of +30 and +20 taken
    /// before a subscription are claimed together as one +50; a +108 snapshot
    /// taken AFTER it is never reached, because that event's gap was zero and it
    /// never got here.
    /// </para>
    /// <para>
    /// <b>That prefix rule is exactly what makes the pairing robust against
    /// same-millisecond writes.</b> A mark is claimed because the arithmetic of
    /// the recorded pre-money says it was already in the balance — recorded data,
    /// not a timestamp — so it works even though a mark and the event it prices
    /// are written in one <c>SaveChangesAsync</c> and their ids are unorderable
    /// against each other.
    /// </para>
    /// <para>
    /// Claimed marks are removed, so a later event on the same day sees them as
    /// part of the balance it was priced against instead of claiming them twice.
    /// </para>
    /// </summary>
    public IReadOnlyList<PoolMarkRow>? Claim(DateOnly date, decimal gap)
    {
        if (!_unclaimedByDate.TryGetValue(date, out List<PoolMarkRow>? onThatDay))
        {
            return null;
        }

        decimal running = 0m;

        for (int i = 0; i < onThatDay.Count; i++)
        {
            running += onThatDay[i].Signed;

            if (Math.Abs(running - gap) <= MoneyTolerance)
            {
                PoolMarkRow[] claimed = [.. onThatDay.Take(i + 1)];
                onThatDay.RemoveRange(0, i + 1);
                return claimed;
            }
        }

        return null;
    }
}
