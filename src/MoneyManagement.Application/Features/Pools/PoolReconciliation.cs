using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Replays a pool's ledger against the account it sits in and reports where the
/// two disagree.
/// <para>
/// <b>Reports; never corrects, never throws.</b> Each finding has more than one
/// possible cause and only the user knows which record is the wrong one — a
/// handler that "repaired" the cheaper side would destroy the evidence that a
/// third party's stake had been mispriced.
/// </para>
/// <para>
/// Four checks, deliberately:
/// </para>
/// <list type="number">
/// <item>
/// <b>Unaccounted money.</b> Net worth reads a pooled account as
/// <c>value × ownerFraction</c>, so any cash on it that neither mints nor burns
/// units is silently shared pro-rata with the outside investors. The write slice
/// guards every path that could produce one; a row here means a guard was
/// bypassed or the row predates the guards.
/// </item>
/// <item>
/// <b><c>Σ participantUnits == totalUnits</c>.</b> Assertable only because the
/// owner holds REAL units instead of being modelled as a residual — as a
/// residual it would be true by construction and worth nothing.
/// </item>
/// <item>
/// <b>Recorded pre-money vs. re-derived pre-money.</b> A mismatch means somebody
/// moved, added or deleted a row dated on or before a date units had already
/// been priced at.
/// </item>
/// <item>
/// <b>Unit events claiming cash the account never saw.</b> The mirror image of
/// the first check, and the ONLY one that can catch a phantom backfill: the
/// pre-money replay deliberately gives up on back-dated events, so a
/// <c>CreatePool</c> backfill claiming money that never landed would otherwise
/// pass every check while minting units against it.
/// </item>
/// </list>
/// <para>
/// <b>The tautology that is NOT checked:</b> <c>Σ units × nav == poolValue</c>.
/// <c>nav</c> is DEFINED as <c>poolValue ÷ units</c>, so that identity holds by
/// construction and proves nothing about the data.
/// </para>
/// </summary>
internal static class PoolReconciliation
{
    /// <summary>
    /// Half a cent. Everything compared here is money at <c>numeric(18,2)</c>,
    /// so a real disagreement is at least one cent and anything smaller is
    /// rounding.
    /// </summary>
    private const decimal MoneyTolerance = PoolUnitEvent.CashReconciliationTolerance;

    /// <param name="pool">The pool being replayed.</param>
    /// <param name="snapshot">The pool folded at <paramref name="asOf"/>, for the units identity.</param>
    /// <param name="events">The pool's whole ledger, unordered.</param>
    /// <param name="accountRows">
    /// Every NON-DELETED transaction on the pool's account. Passed in rather
    /// than queried so this stays pure and testable.
    /// </param>
    /// <param name="balanceAsOf">
    /// The account's derived balance at an arbitrary date — normally
    /// <c>AccountBalanceLedger.NativeBalanceAsOf</c>, bound to the pool's
    /// account. A delegate so the same seam the dashboard uses is the one
    /// replayed here.
    /// </param>
    /// <param name="asOf">Today. Rows dated after it are the caller's problem, not this check's.</param>
    public static PoolReconciliationDto Build(
        Pool pool,
        PoolSnapshot snapshot,
        IReadOnlyList<PoolUnitEvent> events,
        IReadOnlyList<PoolAccountRow> accountRows,
        Func<DateOnly, decimal> balanceAsOf,
        DateOnly asOf)
    {
        List<PoolUnitEvent> ordered =
            [.. events.OrderBy(e => e.OccurredOn).ThenBy(e => e.Id)];

        (IReadOnlyList<UnmatchedPoolTransactionDto> unmatched,
            IReadOnlyList<UnbackedPoolCashClaimDto> unbackedClaims) =
            FindUnaccountedMoney(pool, ordered, accountRows);

        decimal participantUnits = 0m;
        foreach (PoolPosition position in snapshot.Positions)
        {
            participantUnits += position.Units;
        }

        decimal unitsDrift = participantUnits - snapshot.TotalUnits;
        bool unitsBalance = Math.Abs(unitsDrift) <= PoolUnitEvent.UnitsDustTolerance;

        IReadOnlyList<PoolValueDriftDto> valueDrifts =
            FindValueDrifts(ordered, accountRows, balanceAsOf, asOf);

        return new PoolReconciliationDto(
            IsClean: unmatched.Count == 0
                && unitsBalance
                && valueDrifts.Count == 0
                && unbackedClaims.Count == 0,
            unmatched,
            participantUnits,
            snapshot.TotalUnits,
            unitsDrift,
            unitsBalance,
            valueDrifts,
            unbackedClaims);
    }

    /// <summary>
    /// The two halves of the money check, matched against each other in one
    /// pass: transactions on the pool account that no unit event accounts for,
    /// and unit events claiming cash the account never saw.
    /// <para>
    /// <b>Adjustments are excluded by design.</b> The re-pricing mark is an
    /// <c>IsAdjustment</c> row shaped exactly like a hand-typed snapshot, and it
    /// deliberately carries no unit event — marking the account to what the
    /// exchange holds moves everybody's stake pro-rata, which is correct and
    /// needs no ledger entry.
    /// </para>
    /// <para>
    /// <b>"Accounted for" is not the same as "linked".</b> A back-dated
    /// subscription replayed at pool creation normally has
    /// <c>WriteMovementTransaction = false</c> precisely BECAUSE the arrival was
    /// already entered on the account — synthesizing a second Income leg would
    /// double it. That pre-existing row is accounted for by a unit event it has
    /// no foreign key to, so a link-only check would flag every pool that was
    /// set up from history. Unlinked events that claim cash moved
    /// (<c>SettledOn</c> set) are therefore matched to rows by date, direction
    /// and amount, one for one. An UNPAID distribution has no settlement date and
    /// so matches nothing — correctly, since its cash has not left yet.
    /// </para>
    /// <para>
    /// <b>Rows dated on or before INCEPTION are already priced into the seed</b>
    /// — the owner's existing balance simply became units, so nothing on or
    /// before that day needs a ledger entry of its own. They are skipped, but
    /// only AFTER being offered to the claim list: a pool bootstrapped from
    /// history can legitimately have a backfilled subscription landing on the
    /// inception date itself, and consuming its claim here is what stops the
    /// same event being reported as a phantom below.
    /// </para>
    /// <para>
    /// <b>Claims nothing matched are the fourth finding.</b> A settled unit event
    /// says cash moved on a date, in a direction, for an amount; if no row on the
    /// account says the same thing, the event minted or burned units against
    /// money that never existed. Dropping the leftovers (as this did) makes a
    /// back-dated phantom backfill invisible to every check, because the
    /// pre-money replay skips back-dated events on purpose.
    /// </para>
    /// </summary>
    private static (IReadOnlyList<UnmatchedPoolTransactionDto> Unmatched,
        IReadOnlyList<UnbackedPoolCashClaimDto> UnbackedClaims) FindUnaccountedMoney(
        Pool pool,
        IReadOnlyList<PoolUnitEvent> ordered,
        IReadOnlyList<PoolAccountRow> accountRows)
    {
        var linkedTransactionIds = new HashSet<Guid>();
        var unlinkedCashClaims = new List<CashClaim>();

        foreach (PoolUnitEvent unitEvent in ordered)
        {
            if (unitEvent.MovementTransactionId is Guid transactionId)
            {
                linkedTransactionIds.Add(transactionId);
                continue;
            }

            if (unitEvent.Cash is not { } cash || unitEvent.SettledOn is not DateOnly settledOn)
            {
                continue;
            }

            unlinkedCashClaims.Add(
                new CashClaim(unitEvent.Id, settledOn, DirectionOf(unitEvent.Kind), cash.Amount));
        }

        var unmatched = new List<UnmatchedPoolTransactionDto>();

        foreach (PoolAccountRow row in accountRows.OrderBy(r => r.Date).ThenBy(r => r.Id))
        {
            if (row.IsAdjustment || linkedTransactionIds.Contains(row.Id))
            {
                continue;
            }

            int claimIndex = unlinkedCashClaims.FindIndex(c =>
                c.Date == row.Date
                && c.Direction == row.Direction
                && Math.Abs(c.Amount - row.Amount) <= MoneyTolerance);

            if (claimIndex >= 0)
            {
                // One claim, one row. Removing it stops two identical
                // subscriptions from being explained by a single unit event.
                unlinkedCashClaims.RemoveAt(claimIndex);
                continue;
            }

            // Inception day and earlier is the seed's territory: the balance
            // standing there BECAME the owner's units, so no row up to and
            // including that date needs an event of its own. Checked after the
            // claim match, never before, so a backfilled inception-day
            // subscription consumes its claim instead of being reported as a
            // phantom.
            if (row.Date <= pool.InceptionDate)
            {
                continue;
            }

            unmatched.Add(new UnmatchedPoolTransactionDto(
                row.Id,
                row.Date,
                row.Description,
                row.Direction,
                row.Amount,
                row.Currency,
                row.IsTransfer));
        }

        // Whatever is still holding a claim describes cash the account never
        // saw. Reported, never silently dropped.
        var unbacked = new List<UnbackedPoolCashClaimDto>(unlinkedCashClaims.Count);
        foreach (CashClaim claim in unlinkedCashClaims)
        {
            unbacked.Add(new UnbackedPoolCashClaimDto(
                claim.EventId,
                claim.Date,
                claim.Direction,
                claim.Amount));
        }

        return (unmatched, unbacked);
    }

    /// <summary>
    /// Re-derives each event's <c>PoolValuePreMoney</c> from the account's rows
    /// and reports the ones that no longer reproduce.
    /// <para>
    /// The walk itself lives in <see cref="PoolPreMoneyReplay"/>, shared with
    /// the account-detail card's mark attribution — every subtlety it carries
    /// (a close replayed as a unit, marks folded forward and CLAIMED rather than
    /// subtracted backwards, seeds and back-dated backfills skipped) is
    /// documented there. What is left here is the tripwire's own reading of the
    /// result: an event that could not be reproduced is a finding, and an event
    /// that was never checked is not.
    /// </para>
    /// <para>
    /// <b>The claim is not a weakening.</b> The design's own rule is that marks
    /// carry no unit event — "marking the account to what the exchange holds
    /// moves everybody's stake pro-rata, which is correct and needs no ledger
    /// entry" — and <see cref="FindUnaccountedMoney"/> excludes them for exactly
    /// that reason. An end-of-day subtraction would quietly contradict it. What
    /// this check still pins is every OTHER row, and the marks in aggregate:
    /// delete the one that justified a price and the claim finds nothing to
    /// match, which is the drift this exists to report.
    /// </para>
    /// </summary>
    private static IReadOnlyList<PoolValueDriftDto> FindValueDrifts(
        IReadOnlyList<PoolUnitEvent> ordered,
        IReadOnlyList<PoolAccountRow> accountRows,
        Func<DateOnly, decimal> balanceAsOf,
        DateOnly asOf)
    {
        var drifts = new List<PoolValueDriftDto>();

        IReadOnlyList<PoolPricedEvent> priced = PoolPreMoneyReplay.Run(
            ordered,
            PoolMarkLedger.FromAccountRows(accountRows),
            balanceAsOf,
            asOf);

        foreach (PoolPricedEvent replayed in priced)
        {
            if (replayed.Reproduces)
            {
                continue;
            }

            drifts.Add(new PoolValueDriftDto(
                replayed.Event.Id,
                replayed.Event.OccurredOn,
                replayed.Event.Kind,
                replayed.Event.PoolValuePreMoney,
                replayed.DerivedPreMoney,
                replayed.Drift));
        }

        return drifts;
    }

    /// <summary>The direction the synthesized money row carries. Mirrors <see cref="PoolMovements"/>.</summary>
    private static TransactionDirection DirectionOf(PoolUnitEventKind kind) =>
        kind == PoolUnitEventKind.Subscription
            ? TransactionDirection.Income
            : TransactionDirection.Expense;

    /// <param name="EventId">The unit event making the claim, so a leftover can name itself.</param>
    private readonly record struct CashClaim(
        Guid EventId,
        DateOnly Date,
        TransactionDirection Direction,
        decimal Amount);
}

/// <summary>
/// One non-deleted transaction on the pool's account, reduced to the columns
/// the reconciliation needs.
/// </summary>
internal readonly record struct PoolAccountRow(
    Guid Id,
    DateOnly Date,
    string Description,
    TransactionDirection Direction,
    decimal Amount,
    string Currency,
    bool IsTransfer,
    bool IsAdjustment);
