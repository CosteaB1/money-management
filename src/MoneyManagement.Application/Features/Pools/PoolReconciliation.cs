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
/// Five checks, deliberately:
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
/// the first check: the pre-money replay deliberately gives up on back-dated
/// events, so a <c>CreatePool</c> backfill claiming money that never landed
/// would otherwise mint units against it and still pass the first three.
/// </item>
/// <item>
/// <b>The ledger's predicted account balance vs. the real one.</b> The whole
/// ledger folded into ONE number and compared with the account's derived
/// balance. Where the four checks above each look for a particular shape of
/// wrongness, this one asserts the arithmetic that has to hold no matter what
/// shape the wrongness took — which is exactly why it survives the matching
/// subtleties the fourth check depends on. See
/// <see cref="CheckBalanceIdentity"/>.
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

        // Measured ONCE and handed to both money checks, so the two can never
        // disagree about how much of inception day belongs to the seed.
        SeedTerritory seed = MeasureSeedTerritory(pool, ordered, accountRows, balanceAsOf);

        (IReadOnlyList<UnmatchedPoolTransactionDto> unmatched,
            IReadOnlyList<UnbackedPoolCashClaimDto> unbackedClaims) =
            FindUnaccountedMoney(pool, ordered, accountRows, seed);

        decimal participantUnits = 0m;
        foreach (PoolPosition position in snapshot.Positions)
        {
            participantUnits += position.Units;
        }

        decimal unitsDrift = participantUnits - snapshot.TotalUnits;
        bool unitsBalance = Math.Abs(unitsDrift) <= PoolUnitEvent.UnitsDustTolerance;

        IReadOnlyList<PoolValueDriftDto> valueDrifts =
            FindValueDrifts(ordered, accountRows, balanceAsOf, asOf);

        BalanceIdentity balance = CheckBalanceIdentity(pool, ordered, accountRows, seed, balanceAsOf, asOf);

        return new PoolReconciliationDto(
            IsClean: unmatched.Count == 0
                && unitsBalance
                && valueDrifts.Count == 0
                && unbackedClaims.Count == 0
                && balance.Reconciles,
            unmatched,
            participantUnits,
            snapshot.TotalUnits,
            unitsDrift,
            unitsBalance,
            valueDrifts,
            unbackedClaims,
            balance.Predicted,
            balance.Derived,
            balance.Drift,
            balance.Reconciles);
    }

    /// <summary>
    /// How much of INCEPTION DAY belongs to the seed, and how much of it does
    /// not. Measured once per reconciliation and shared by both money checks, so
    /// they cannot reach different conclusions about the same row.
    /// <para>
    /// The account held <c>balanceAsOf(inception)</c> that day. Three things can
    /// claim a piece of it: unit events whose money row is already LINKED (their
    /// own leg), the SEED (<c>units x nav</c>, and a seed is struck at a NAV of
    /// exactly one, so that is simply the balance that became the owner's units),
    /// and nothing else. What is left over — the SURPLUS — is money sitting on
    /// the account at inception that the ledger does not yet account for.
    /// </para>
    /// <para>
    /// <b>On a healthy pool the surplus is exactly zero</b>, because
    /// <c>CreatePool</c> writes a catch-up mark that brings the account to the
    /// very value it then seeds. It is positive only when money reached the
    /// account on or before inception day AFTER the seed was struck — which
    /// through the shipped write paths means a re-pricing mark on a pool whose
    /// inception IS today, since every other route into a pooled account is
    /// closed (<see cref="PooledAccountGuard"/>) and <c>AdjustBalance</c> refuses
    /// a back-dated mark outright.
    /// </para>
    /// <para>
    /// <b>Why it is a quantity and not an identity.</b> Both consumers need to
    /// answer "does this event's money exist on the account at inception?", and
    /// at inception two rows of the same amount and direction are genuinely
    /// indistinguishable — that is exactly how a phantom subscription came to
    /// consume the owner's own funding transfer. Counting is the only honest
    /// answer available.
    /// </para>
    /// </summary>
    /// <returns>
    /// The linked backing and the surplus, both rounded to the cent, plus the
    /// allowance an unlinked inception-day claim may draw on.
    /// </returns>
    private static SeedTerritory MeasureSeedTerritory(
        Pool pool,
        IReadOnlyList<PoolUnitEvent> ordered,
        IReadOnlyList<PoolAccountRow> accountRows,
        Func<DateOnly, decimal> balanceAsOf)
    {
        var linkedTransactionIds = new HashSet<Guid>();

        decimal seedValue = 0m;
        decimal unlinkedInflow = 0m;

        foreach (PoolUnitEvent unitEvent in ordered)
        {
            if (unitEvent.Kind == PoolUnitEventKind.Seed)
            {
                seedValue += PoolUnitRegister.RoundMoney(unitEvent.Units * unitEvent.NavPerUnit);
                continue;
            }

            if (unitEvent.MovementTransactionId is Guid transactionId)
            {
                linkedTransactionIds.Add(transactionId);
                continue;
            }

            // Cash an unlinked event says ARRIVED on or before inception day. The
            // ceiling on what such claims may collectively take off the seed.
            if (unitEvent.Cash is { } cash
                && unitEvent.SettledOn is DateOnly settledOn
                && settledOn <= pool.InceptionDate
                && DirectionOf(unitEvent.Kind) == TransactionDirection.Income)
            {
                unlinkedInflow += cash.Amount;
            }
        }

        decimal linkedBacking = 0m;

        foreach (PoolAccountRow row in accountRows)
        {
            if (row.Date <= pool.InceptionDate && linkedTransactionIds.Contains(row.Id))
            {
                linkedBacking += Signed(row);
            }
        }

        decimal balanceAtInception = balanceAsOf(pool.InceptionDate);

        decimal surplus = PoolUnitRegister.RoundMoney(
            balanceAtInception - linkedBacking - seedValue);

        return new SeedTerritory(
            balanceAtInception,
            PoolUnitRegister.RoundMoney(linkedBacking),
            surplus,
            ClaimAllowance: Math.Clamp(unlinkedInflow, 0m, Math.Max(0m, surplus)));
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
    /// before that day needs a ledger entry of its own. They are never REPORTED
    /// as unaccounted. They are, however, still offered to the claim list first,
    /// because a pool bootstrapped from history can have a backfilled
    /// subscription landing on the inception date itself, and consuming its own
    /// row is what stops that event being reported as a phantom below.
    /// </para>
    /// <para>
    /// <b>THE SEED'S BACKING IS RESERVED BEFORE ANY CLAIM MAY TOUCH IT.</b> The
    /// old ordering — claim match first, seed skip second, no reservation at all
    /// — is what let a real pool report itself clean while a friend's 1,000 had
    /// never been recorded: the phantom claim (inception day, Income, 1,000)
    /// matched the OWNER'S OWN funding transfer, a different real row, consumed
    /// it, and left nothing to report. Date + direction + amount cannot tell
    /// those two rows apart, and no tie-breaking rule can, because they really
    /// are identical.
    /// </para>
    /// <para>
    /// The distinction that CAN be made is one of quantity rather than identity.
    /// The account held <c>balanceAsOf(inception)</c> on inception day; the seed
    /// claims <c>units x nav</c> of it (a seed is struck at a NAV of exactly one,
    /// so that is just the balance that became the owner's units), and every unit
    /// event whose money row is already LINKED claims its own. Whatever is left
    /// over is the only money at inception an unlinked claim could be pointing
    /// at, and a claim is matched against a row dated then only while that
    /// surplus can still cover it. On a healthy pool the surplus is exactly zero,
    /// because <c>CreatePool</c> marks the account to the very value it seeds; on
    /// the broken pool it was zero as well, and the phantom got reported.
    /// </para>
    /// <para>
    /// Deliberately a BUDGET and not a ban. A ban would also refuse the
    /// legitimate from-history case the ordering exists for, trading one silent
    /// miss for a permanent false alarm. The surplus is measured up front by
    /// <see cref="MeasureSeedTerritory"/>, never as this walk proceeds, so it
    /// cannot depend on whether the owner's row happens to sort before or after a
    /// friend's leg — which in the live pool it did.
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
        IReadOnlyList<PoolAccountRow> accountRows,
        SeedTerritory seed)
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

        // The seed's backing was reserved before this walk started; what is left
        // is the running budget an inception-day claim may draw on.
        decimal inceptionSurplus = seed.Surplus;

        var unmatched = new List<UnmatchedPoolTransactionDto>();

        // Rows in the order they were written.
        foreach (PoolAccountRow row in accountRows.OrderBy(r => r.Date).ThenBy(r => r.Id))
        {
            if (row.IsAdjustment || linkedTransactionIds.Contains(row.Id))
            {
                continue;
            }

            bool insideTheSeedsTerritory = row.Date <= pool.InceptionDate;

            // Inside the seed's territory a claim may only consume money the seed
            // does not already account for. After inception there is nothing to
            // reserve and the match is unrestricted.
            bool claimable = !insideTheSeedsTerritory
                || inceptionSurplus + MoneyTolerance >= row.Amount;

            int claimIndex = claimable
                ? unlinkedCashClaims.FindIndex(c =>
                    c.Date == row.Date
                    && c.Direction == row.Direction
                    && Math.Abs(c.Amount - row.Amount) <= MoneyTolerance)
                : -1;

            if (claimIndex >= 0)
            {
                // One claim, one row. Removing it stops two identical
                // subscriptions from being explained by a single unit event.
                unlinkedCashClaims.RemoveAt(claimIndex);

                if (insideTheSeedsTerritory)
                {
                    inceptionSurplus -= row.Amount;
                }

                continue;
            }

            // Inception day and earlier is the seed's territory: the balance
            // standing there BECAME the owner's units, so no row up to and
            // including that date needs an event of its own.
            if (insideTheSeedsTerritory)
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

    /// <summary>
    /// Folds the WHOLE ledger into one number and compares it with the account's
    /// derived balance.
    /// <para>
    /// The ledger makes a falsifiable claim about the account, and this is it:
    /// start from the balance that became the seed's units on inception day, add
    /// every subscription's cash, take out every redemption's and every SETTLED
    /// distribution's, and move it by every re-pricing mark since. That total has
    /// to be what the account actually holds.
    /// </para>
    /// <para>
    /// <b>Why this exists next to four checks that already look for wrongness:</b>
    /// each of those looks for a SHAPE — an unexplained row, a units mismatch, a
    /// pre-money that no longer re-derives, a claim with no row — and a shape can
    /// be mimicked. A real pool reported itself clean with 3,000 units against a
    /// 2,000 balance because one friend's phantom claim carried the same date,
    /// direction and amount as the owner's own funding transfer and quietly
    /// consumed it. This check does not care which row is which: it predicts
    /// 3,000, the account holds 2,000, and the 1,000 gap IS the missing leg.
    /// </para>
    /// <para>
    /// <b>The inception anchor is derived from the ACCOUNT, not read off the seed
    /// event</b> — and that is the difference between a tripwire and a nuisance.
    /// The two agree on a healthy pool (<c>CreatePool</c> marks the account to the
    /// exact value it seeds), but they part company the moment a mark lands ON
    /// inception day AFTER the seed was struck — create a pool dated today and
    /// record a subscription the same day and <c>PoolMark</c> writes exactly that,
    /// through the shipped write slice with no guard bypassed. Anchored on the
    /// seed's stored value, every such pool would report a gap equal to the mark,
    /// forever. Anchored on <c>balanceAsOf(inception)</c> the mark is simply
    /// inside the anchor and the identity still holds.
    /// </para>
    /// <para>
    /// Four things it must NOT fire on, and why it does not:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>An unpaid distribution.</b> Closed but not settled: the cash is still
    /// sitting in the account. <c>SettledOn</c> is null, so the payout is not
    /// subtracted — the same two-phase rule <c>SnapshotAsOf</c> lives by.
    /// </item>
    /// <item>
    /// <b>Rows predating inception.</b> Whatever history the account carried when
    /// the pool started is inside <c>balanceAsOf(inception)</c>, which is where
    /// the prediction starts. It is never enumerated, so it can never be
    /// double-counted, and an account with years of rows behind it reconciles the
    /// same as an empty one.
    /// </item>
    /// <item>
    /// <b>The marks convention.</b> Marks move the balance and everybody's stake
    /// pro-rata and deliberately carry no unit event, so they are read off the
    /// ACCOUNT rather than looked for in the ledger. Only marks dated after
    /// inception are added; the ones on or before it are already in the anchor.
    /// </item>
    /// <item>
    /// <b>Money rows dated at inception that a unit event owns.</b> A backfill
    /// whose leg lands on inception day is inside the anchor AND inside the cash
    /// sum below, so <see cref="SeedTerritory.LinkedBacking"/> takes it back out
    /// exactly once. Its unlinked twin — a from-history backfill whose arrival was
    /// already typed onto the account — is handled by
    /// <see cref="SeedTerritory.ClaimAllowance"/>, which is capped at the surplus
    /// actually sitting there.
    /// </item>
    /// </list>
    /// <para>
    /// <b>It reads quantities off <see cref="SeedTerritory"/>, never the money
    /// check's verdict.</b> Both come from the same measurement, so on a pool
    /// where the money check is right the two agree; but the allowance is a
    /// ceiling derived from the account's own balance, not "the rows a matcher
    /// decided to accept". Break the matcher entirely and this still predicts
    /// 3,000 against the live pool's 2,000 — which is what "regardless of matching
    /// subtleties" has to mean to be worth adding.
    /// </para>
    /// <para>
    /// <b>What it does NOT assert:</b> that the seed's stored units match the
    /// account. They match by construction — the catch-up mark makes them — and
    /// the only things that can break the equality later (a mark deleted,
    /// pre-inception history edited) are exactly what
    /// <see cref="PoolReconciliationDto.ValueDrifts"/> exists to report. Asserting
    /// it here would restate that finding in a second, vaguer voice.
    /// </para>
    /// </summary>
    /// <param name="seed">
    /// Inception day, measured. Only the ARITHMETIC is taken from it — the linked
    /// backing and the claim allowance — never a decision about which row a claim
    /// was matched to. That independence is the point: this check must survive
    /// the money check being wrong.
    /// </param>
    private static BalanceIdentity CheckBalanceIdentity(
        Pool pool,
        IReadOnlyList<PoolUnitEvent> ordered,
        IReadOnlyList<PoolAccountRow> accountRows,
        SeedTerritory seed,
        Func<DateOnly, decimal> balanceAsOf,
        DateOnly asOf)
    {
        // The balance that became the owner's units: the account as it stood on
        // inception day, less the legs the ledger already owns and the most an
        // unlinked inception-day claim could legitimately be pointing at.
        decimal predicted = seed.BalanceAtInception - seed.LinkedBacking - seed.ClaimAllowance;

        foreach (PoolAccountRow row in accountRows)
        {
            if (row.IsAdjustment && row.Date > pool.InceptionDate && row.Date <= asOf)
            {
                predicted += Signed(row);
            }
        }

        foreach (PoolUnitEvent unitEvent in ordered)
        {
            // Cash that has actually MOVED. Seeds and the two cost kinds carry
            // none; a closed-but-unpaid distribution has no settlement date, and
            // its money is still sitting in the account.
            if (unitEvent.Cash is not { } cash
                || unitEvent.SettledOn is not DateOnly settledOn
                || settledOn > asOf)
            {
                continue;
            }

            predicted += DirectionOf(unitEvent.Kind) == TransactionDirection.Income
                ? cash.Amount
                : -cash.Amount;
        }

        predicted = PoolUnitRegister.RoundMoney(predicted);
        decimal derived = PoolUnitRegister.RoundMoney(balanceAsOf(asOf));

        decimal drift = predicted - derived;

        return new BalanceIdentity(predicted, derived, drift, Math.Abs(drift) <= MoneyTolerance);
    }

    /// <summary>The row's effect on the account's balance, signed.</summary>
    private static decimal Signed(PoolAccountRow row) =>
        row.Direction == TransactionDirection.Income ? row.Amount : -row.Amount;

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

    /// <summary>Inception day, measured. See <see cref="MeasureSeedTerritory"/>.</summary>
    /// <param name="BalanceAtInception">What the account held on inception day.</param>
    /// <param name="LinkedBacking">
    /// Signed total of the rows dated on or before inception that a unit event
    /// already points at. That money is the event's, not the seed's.
    /// </param>
    /// <param name="Surplus">
    /// <c>BalanceAtInception − LinkedBacking − seedValue</c>: money on the account
    /// at inception the ledger does not yet account for. Zero on a healthy pool.
    /// </param>
    /// <param name="ClaimAllowance">
    /// The most that unlinked events claiming an inception-day ARRIVAL can
    /// collectively take off the seed — their total cash, capped at the surplus
    /// actually sitting there. Zero when there is nothing spare, which is what
    /// stopped a phantom subscription consuming the owner's funding transfer.
    /// </param>
    private readonly record struct SeedTerritory(
        decimal BalanceAtInception,
        decimal LinkedBacking,
        decimal Surplus,
        decimal ClaimAllowance);

    /// <param name="Predicted">What the ledger says the account should hold.</param>
    /// <param name="Derived">What the account's own rows say it holds.</param>
    /// <param name="Drift"><c>Predicted − Derived</c>.</param>
    /// <param name="Reconciles">Whether the two agree to within half a cent.</param>
    private readonly record struct BalanceIdentity(
        decimal Predicted,
        decimal Derived,
        decimal Drift,
        bool Reconciles);
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
