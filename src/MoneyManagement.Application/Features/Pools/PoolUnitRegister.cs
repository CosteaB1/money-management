using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// The pool's unit ledger, folded. Given one pool's participants and unit
/// events it answers, at any date: units per participant, total units
/// outstanding, <c>navPerUnit</c>, and each participant's capital base.
/// <para>
/// <b>Pure and deterministic — nothing here touches the database.</b> The
/// caller loads participants + events once (see <c>PoolWriteContext</c>) and
/// folds in memory. This type is shared by the write slice, the read slice and
/// the ownership source precisely so those three can never disagree about what
/// somebody owns; every "just compute it inline here" copy is a future
/// divergence between the dashboard and the pool page.
/// </para>
/// <para>
/// The fold is order-independent (plain sums), so an unsorted event list is
/// safe — unlike <c>AccountOwnership.Points</c>, which is a step function and
/// depends on ordering.
/// </para>
/// </summary>
internal sealed class PoolUnitRegister
{
    /// <summary>
    /// Decimal places units and NAV are stored at (<c>numeric(28,12)</c>, see
    /// <c>PoolUnitEventConfiguration</c>). Everything computed here is rounded
    /// to this scale BEFORE it is handed to <see cref="PoolUnitEvent.Create"/>,
    /// so the in-memory entity and the row Postgres stores agree exactly.
    /// Skipping that leaves the database silently rounding a value the
    /// application already validated.
    /// </summary>
    public const int UnitScale = 12;

    /// <summary>Decimal places money is stored at (<c>numeric(18,2)</c>).</summary>
    public const int MoneyScale = 2;

    private readonly IReadOnlyList<PoolParticipant> _participants;
    private readonly IReadOnlyList<PoolUnitEvent> _events;

    private PoolUnitRegister(IReadOnlyList<PoolParticipant> participants, IReadOnlyList<PoolUnitEvent> events)
    {
        _participants = participants;
        _events = events;
    }

    /// <summary>
    /// Folds one pool's roster and ledger.
    /// <para>
    /// <paramref name="participants"/> must include ARCHIVED rows. The whole
    /// model rests on <c>Σ participantUnits == totalUnits</c>; dropping an
    /// archived row from that sum hands their stake to the owner silently.
    /// (<c>PoolParticipant</c> deliberately has no global query filter for the
    /// same reason.)
    /// </para>
    /// </summary>
    public static PoolUnitRegister Create(
        IEnumerable<PoolParticipant> participants,
        IEnumerable<PoolUnitEvent> events) =>
        new([.. participants], [.. events]);

    /// <summary>Rounds to the units/NAV storage scale.</summary>
    public static decimal RoundUnits(decimal units) =>
        Math.Round(units, UnitScale, MidpointRounding.AwayFromZero);

    /// <summary>Rounds to the money storage scale.</summary>
    public static decimal RoundMoney(decimal amount) =>
        Math.Round(amount, MoneyScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The share of a pooled account that is the USER's, given the two unit
    /// totals — the ONLY arithmetic behind
    /// <see cref="Abstractions.NetWorth.OwnedFractionPoint.Fraction"/>.
    /// <para>
    /// <b>Unit counts only.</b> No balance, no NAV, no transaction. That is what
    /// keeps <see cref="PoolAccountOwnershipSource"/> at one flat query with
    /// zero FX round-trips, and it is why a NAV later found to be wrong
    /// misprices nothing retroactively.
    /// </para>
    /// <para>
    /// <b>No units outstanding =&gt; 1.0, never a division.</b> Nobody else holds
    /// a claim on the account, so it is wholly the user's — the same default
    /// <c>AccountOwnershipLedger</c> applies to an account no source mentions.
    /// This deliberately differs from <see cref="PoolPosition.OwnedFraction"/>,
    /// which answers a different question ("what share of nothing does this
    /// participant hold?" — zero).
    /// </para>
    /// <para>
    /// Clamped to <c>[0, 1]</c>. <see cref="AccountOwnership"/> does not clamp and
    /// says so: the range is the PRODUCER's guarantee, and this is the producer.
    /// Only a corrupt ledger (a participant driven negative out of band) can
    /// leave the range, and reporting more wealth than the account actually holds
    /// is strictly worse than reporting all of it. The corruption itself surfaces
    /// on the pool detail page's reconciliation tripwire, which is the place
    /// designed to say so out loud.
    /// </para>
    /// </summary>
    public static decimal OwnedFractionOf(decimal ownerUnits, decimal totalUnits)
    {
        if (totalUnits <= PoolUnitEvent.UnitsDustTolerance)
        {
            return 1m;
        }

        decimal fraction = ownerUnits / totalUnits;
        return Math.Clamp(fraction, 0m, 1m);
    }

    /// <summary>
    /// Folds one pool's ledger into the dated owner-fraction step function the
    /// net-worth seam consumes.
    /// <para>
    /// Lives here, next to <see cref="SnapshotAsOf"/>, so the ownership source
    /// and the read queries can never disagree about who owns what: both take
    /// their direction rule from <see cref="PoolUnitEvent.KindIncreasesUnits"/>
    /// and their never-divide rule from <see cref="OwnedFractionOf"/>.
    /// </para>
    /// <para>
    /// <b>Every movement takes effect on the day its CASH moved</b>
    /// (<see cref="PoolUnitMovement.EffectiveOn"/>) — which is
    /// <see cref="PoolUnitMovement.OccurredOn"/> for every kind except a
    /// distribution, where it is <see cref="PoolUnitMovement.SettledOn"/>. That
    /// asymmetry is not a nicety; it is the whole reason net worth stays honest
    /// between close and settle. The consumer multiplies this fraction into the
    /// account's RAW balance, and between those two dates the balance still
    /// holds the friends' payout: retire the units at the close and the owner is
    /// credited with <c>ownerFraction × unpaidDistributionCash</c> of somebody
    /// else's money — and a month-end close pins that error onto that trend
    /// point FOREVER, because the payment row is dated after the as-of cutoff.
    /// Retiring at settle instead makes paying a distribution move net worth by
    /// EXACTLY ZERO: gross falls by the cash and the fraction rises by precisely
    /// enough to leave the owner's share where it was.
    /// </para>
    /// <para>
    /// A closed-but-unpaid distribution therefore has no effective date at all
    /// and contributes NOTHING — its units stay outstanding until the transfer
    /// goes out. <see cref="SnapshotAsOf"/> deliberately takes the opposite
    /// convention: it retires those units at the close and nets the owed cash
    /// out of pool value instead. The two price the same money identically
    /// (<c>balance × fraction == units × nav</c>), which is why the dashboard
    /// and the pool page agree to the cent at all times; see
    /// <c>PoolDto.OwnerFraction</c> for why the two FRACTIONS nevertheless
    /// differ while a payout is outstanding.
    /// </para>
    /// <para>
    /// Emits <b>exactly one point per distinct effective date</b>, carrying the
    /// END-OF-DAY state — every movement effective that day has already been
    /// folded in, so the last one of the day wins. That matches
    /// <c>AccountOwnership.OwnedFractionAsOf</c>'s inclusive cutoff and
    /// <c>AccountBalanceLedger.NativeBalanceAsOf</c>'s <c>date &lt;= asOf</c>
    /// rule: a subscription landing on a month-end raises the balance and
    /// dilutes the owner on the same trend point, instead of inflating their
    /// share for exactly one month.
    /// </para>
    /// <para>
    /// The result is OLDEST-FIRST. That ordering is the producer contract
    /// <c>AccountOwnership.OwnedFractionAsOf</c> relies on (it breaks at the
    /// first future point), so the sort happens here rather than being assumed
    /// of the caller's query.
    /// </para>
    /// </summary>
    public static IReadOnlyList<OwnedFractionPoint> OwnershipCurve(IEnumerable<PoolUnitMovement> movements) =>
        OwnershipTimeline(movements).Curve;

    /// <summary>
    /// The same fold as <see cref="OwnershipCurve"/>, kept at MOVEMENT
    /// granularity as well as per-day.
    /// <para>
    /// The dated curve is all net worth ever needs: value and fraction share an
    /// end-of-day cutoff, so one point per date is exactly right there. The
    /// account-detail Performance card needs one thing the curve deliberately
    /// cannot express — the fraction in force PART WAY THROUGH a day, because a
    /// re-pricing mark is struck at an instant, not at a date, and a day can
    /// hold a capital event on either side of one. Both readings come off this
    /// single fold so the card and the dashboard cannot drift apart; see
    /// <see cref="PoolOwnershipTimeline"/> for what each answers.
    /// </para>
    /// </summary>
    public static PoolOwnershipTimeline OwnershipTimeline(IEnumerable<PoolUnitMovement> movements)
    {
        // Movements whose cash has not moved yet drop out entirely - today that
        // is exactly the closed-but-unpaid distributions, whose units stay
        // outstanding until the transfer goes out.
        var effective = new List<(DateOnly On, PoolUnitMovement Movement)>();

        foreach (PoolUnitMovement movement in movements)
        {
            if (movement.EffectiveOn is DateOnly on)
            {
                effective.Add((on, movement));
            }
        }

        // (EffectiveOn, EventId). Ids are UUIDv7, so the tiebreak is the order
        // the events were actually recorded - which is the order the write
        // handlers priced them in.
        List<(DateOnly On, PoolUnitMovement Movement)> ordered =
            [.. effective.OrderBy(e => e.On).ThenBy(e => e.Movement.EventId)];

        var steps = new List<OwnedFractionStep>(ordered.Count);
        var points = new List<OwnedFractionPoint>();

        decimal ownerUnits = 0m;
        decimal totalUnits = 0m;

        for (int i = 0; i < ordered.Count; i++)
        {
            (DateOnly on, PoolUnitMovement movement) = ordered[i];

            decimal delta = PoolUnitEvent.KindIncreasesUnits(movement.Kind)
                ? movement.Units
                : -movement.Units;

            totalUnits += delta;
            if (movement.ParticipantIsOwner)
            {
                ownerUnits += delta;
            }

            decimal fraction = OwnedFractionOf(ownerUnits, totalUnits);
            steps.Add(new OwnedFractionStep(on, movement.EventId, fraction));

            // Keep folding while the next movement takes effect on the same day;
            // the point emitted below is therefore the state after ALL of that
            // day's movements.
            if (i + 1 < ordered.Count && ordered[i + 1].On == on)
            {
                continue;
            }

            points.Add(new OwnedFractionPoint(on, fraction));
        }

        return new PoolOwnershipTimeline(steps, points);
    }

    /// <summary>
    /// Rounds a payable amount DOWN to the cent. Used for distributable profit
    /// so the default payout can never exceed the amount actually owed (which
    /// would fail the <c>cash &lt;= distributable</c> check it is the default
    /// for).
    /// </summary>
    public static decimal FloorMoney(decimal amount) =>
        Math.Round(amount, MoneyScale, MidpointRounding.ToZero);

    /// <summary>
    /// The pool's state on <paramref name="asOf"/>.
    /// </summary>
    /// <param name="asOf">
    /// End-of-day cutoff: an event dated exactly <paramref name="asOf"/>
    /// counts, matching <c>AccountBalanceLedger.NativeBalanceAsOf</c>. The value
    /// and the units MUST share one cutoff or a subscription landing on a
    /// month-end prices against a balance that already contains it.
    /// </param>
    /// <param name="accountBalance">
    /// The pooled account's DERIVED balance in its own currency on the same
    /// date — <c>AccountBalanceLedger.NativeBalanceAsOf(account, asOf)</c>. Passed
    /// in rather than queried so the fold stays pure.
    /// </param>
    public PoolSnapshot SnapshotAsOf(DateOnly asOf, decimal accountBalance)
    {
        Dictionary<Guid, decimal> unitsByParticipant = [];
        Dictionary<Guid, decimal> capitalByParticipant = [];

        decimal totalUnits = 0m;
        decimal unpaidDistributionCash = 0m;

        foreach (PoolUnitEvent unitEvent in _events)
        {
            if (unitEvent.OccurredOn > asOf)
            {
                continue;
            }

            decimal delta = unitEvent.UnitsDelta;
            unitsByParticipant[unitEvent.ParticipantId] =
                unitsByParticipant.GetValueOrDefault(unitEvent.ParticipantId) + delta;

            // Total is summed from the EVENTS, not from the positions, so an
            // event pointing at a participant row that somehow isn't in the
            // roster still shows up in the total instead of vanishing. (The FK
            // makes that impossible in the database; this keeps the in-memory
            // fold honest for unit tests too.)
            totalUnits += delta;

            decimal cash = unitEvent.Cash?.Amount ?? 0m;

            // capitalBase = Σ subscription cash − Σ redemption cash.
            //
            // DISTRIBUTIONS AND COST EVENTS DELIBERATELY DO NOT TOUCH IT. That
            // single omission is what makes the high-water mark free and
            // stored-field-less: `distributable = max(0, stake − capitalBase)`
            // pays nothing below basis, so a green month after a drawdown
            // correctly pays zero, and a participant who SKIPS a payout just
            // leaves their distributable accumulating — reinvestment needs no
            // code at all.
            switch (unitEvent.Kind)
            {
                case PoolUnitEventKind.Subscription:
                    capitalByParticipant[unitEvent.ParticipantId] =
                        capitalByParticipant.GetValueOrDefault(unitEvent.ParticipantId) + cash;
                    break;

                case PoolUnitEventKind.Redemption:
                    capitalByParticipant[unitEvent.ParticipantId] =
                        capitalByParticipant.GetValueOrDefault(unitEvent.ParticipantId) - cash;
                    break;

                case PoolUnitEventKind.Distribution:
                    // Unpaid AS OF this date: still null, or settled only later.
                    // The month closes at month-end but the money physically
                    // leaves at the start of the next one; between the two the
                    // cash is owed AND still sitting in the account. Without
                    // this subtraction it counts as pool value a second time and
                    // the friends get paid twice on the same profit, every month.
                    if (unitEvent.SettledOn is not DateOnly settled || settled > asOf)
                    {
                        unpaidDistributionCash += cash;
                    }

                    break;

                case PoolUnitEventKind.Seed:
                case PoolUnitEventKind.CostShare:
                case PoolUnitEventKind.CostRecovery:
                default:
                    break;
            }
        }

        decimal poolValue = accountBalance - unpaidDistributionCash;

        // No units => no price. And a pool whose value has fallen to zero has no
        // price either: NAV comes out 0 - or negative, after a mark against an
        // account the app derives below zero - and EVERY caller divides by it, so
        // a units-only guard leaves a DivideByZeroException one bad snapshot
        // away. Both cases resolve to null, which RequireNav turns into
        // PoolErrors.NavUndefined: callers must fail loudly rather than divide,
        // default to 1.0, or "just use the pool value".
        //
        // The rounding happens BEFORE the sign test on purpose - a value small
        // enough to quantize to zero at numeric(28,12) is a zero price, not a
        // small one, and it divides just as badly.
        decimal? navPerUnit = null;

        if (totalUnits > PoolUnitEvent.UnitsDustTolerance)
        {
            decimal candidate = RoundUnits(poolValue / totalUnits);
            if (candidate > 0m)
            {
                navPerUnit = candidate;
            }
        }

        List<PoolPosition> positions = new(_participants.Count);

        // Deterministic, owner first: the order shows up in API responses and in
        // assertion messages.
        IOrderedEnumerable<PoolParticipant> ordered = _participants
            .OrderByDescending(p => p.IsOwner)
            .ThenBy(p => p.JoinedOn)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Id);

        foreach (PoolParticipant participant in ordered)
        {
            decimal units = unitsByParticipant.GetValueOrDefault(participant.Id);
            decimal capitalBase = capitalByParticipant.GetValueOrDefault(participant.Id);

            decimal? stake = null;
            decimal? distributable = null;

            if (navPerUnit is decimal nav)
            {
                decimal grossStake = units * nav;
                decimal roundedStake = RoundMoney(grossStake);

                // Computed from the UNROUNDED stake and then floored, so the
                // default payout is always <= what is actually owed.
                //
                // THE BASE IS FLOORED AT ZERO HERE, AND ONLY HERE. The reported
                // CapitalBase stays Σ subscription cash − Σ redemption cash, and
                // that legitimately goes NEGATIVE for the owner, who seeds units
                // with no cash leg and then redeems to Bybit. Fed straight into
                // stake - base it would make the distributable EXCEED the whole
                // stake, and a close would try to retire more units than the
                // participant holds. Clamping is also the economically right
                // answer: somebody who has already taken out more cash than they
                // put in has recovered their capital, so their entire remaining
                // stake is profit - which is exactly what the high-water rule
                // "no payment below their investment" means.
                //
                // Capped at the stake as well, so Distributable <= Stake holds by
                // CONSTRUCTION for every position - including a corrupt one driven
                // to negative units out of band. A structural cap beats an
                // assertion the pool page would crash on at exactly the moment its
                // reconciliation tripwire is trying to explain the corruption.
                stake = roundedStake;
                distributable = Math.Min(
                    roundedStake,
                    FloorMoney(Math.Max(0m, grossStake - Math.Max(0m, capitalBase))));
            }

            decimal fraction = totalUnits > PoolUnitEvent.UnitsDustTolerance
                ? units / totalUnits
                : 0m;

            positions.Add(new PoolPosition(
                participant.Id,
                participant.Name,
                participant.IsOwner,
                participant.IsArchived,
                units,
                capitalBase,
                stake,
                distributable,
                fraction));
        }

        return new PoolSnapshot(
            asOf,
            accountBalance,
            unpaidDistributionCash,
            poolValue,
            totalUnits,
            navPerUnit,
            positions);
    }
}

/// <summary>The pool's state on one date, as folded by <see cref="PoolUnitRegister"/>.</summary>
/// <param name="AsOf">The date every figure below is evaluated at (end-of-day).</param>
/// <param name="AccountBalance">The pooled account's derived balance, in the pool's currency.</param>
/// <param name="UnpaidDistributionCash">Closed-but-unpaid distributions still sitting in the account.</param>
/// <param name="PoolValue"><c>AccountBalance − UnpaidDistributionCash</c>. The value units are priced against.</param>
/// <param name="TotalUnits">Units outstanding across every participant, archived included.</param>
/// <param name="NavPerUnit">
/// <c>PoolValue ÷ TotalUnits</c>, rounded to the storage scale — or <c>null</c>
/// when no units are outstanding. Never substitute a value for the null; see
/// <see cref="RequireNav"/>.
/// </param>
/// <param name="Positions">One row per participant, owner first.</param>
internal sealed record PoolSnapshot(
    DateOnly AsOf,
    decimal AccountBalance,
    decimal UnpaidDistributionCash,
    decimal PoolValue,
    decimal TotalUnits,
    decimal? NavPerUnit,
    IReadOnlyList<PoolPosition> Positions)
{
    /// <summary>The NAV, or a failure. The only sanctioned way to read it.</summary>
    public Result<decimal> RequireNav() =>
        NavPerUnit is decimal nav
            ? Result.Success(nav)
            : Result.Failure<decimal>(PoolErrors.NavUndefined);

    public PoolPosition? Find(Guid participantId) =>
        Positions.FirstOrDefault(p => p.ParticipantId == participantId);
}

/// <summary>One participant's position in a <see cref="PoolSnapshot"/>.</summary>
/// <param name="ParticipantId">The participant row.</param>
/// <param name="Name">Their display name at the time of the fold.</param>
/// <param name="IsOwner">Whether this is the user's own row.</param>
/// <param name="IsArchived">Archived participants still appear — they are part of Σ units.</param>
/// <param name="Units">Σ of their signed unit deltas up to the snapshot date.</param>
/// <param name="CapitalBase">
/// <c>Σ subscription cash − Σ redemption cash</c>. Distributions and cost events
/// never move it, which IS the high-water mark.
/// </param>
/// <param name="Stake"><c>Units × NavPerUnit</c>, to the cent — <c>null</c> when NAV is undefined.</param>
/// <param name="Distributable">
/// <c>max(0, stake − max(0, capitalBase))</c>, floored to the cent and capped at
/// <paramref name="Stake"/> — <c>null</c> when NAV is undefined. Zero below
/// basis: that is the high-water mark, with no stored field and no reset logic.
/// <para>
/// <b>The base is floored at zero for this figure only</b>;
/// <paramref name="CapitalBase"/> itself keeps its signed definition. A
/// participant who has withdrawn more cash than they subscribed has already
/// recovered their capital, so all of what is left is profit — and without the
/// floor the distributable would exceed the stake and a close would try to
/// retire units nobody holds.
/// </para>
/// <para>
/// <b>Read this with care for the OWNER row.</b> A seed carries no cash, so the
/// owner's capital base is 0 and their "distributable" is their entire stake.
/// That is the formula being honest, not an amount anybody should be offered a
/// button for: the owner's profit stays in and grows their share, and they take
/// value out with a redemption. <c>CloseDistributionCommandHandler</c>'s default
/// payout set excludes the owner for exactly this reason, and any UI should too.
/// </para>
/// </param>
/// <param name="OwnedFraction">
/// <c>Units ÷ TotalUnits</c>, unrounded. Unit counts only — NAV never enters the
/// fraction, so a NAV later found to be wrong misprices nothing retroactively.
/// <para>
/// <b>ZERO when no units are outstanding</b>, and that deliberately disagrees
/// with <see cref="PoolUnitRegister.OwnedFractionOf"/>, which answers 1.0 for
/// the same wound-down pool. They answer different questions: a share of
/// nothing is zero, while the share of the ACCOUNT that belongs to the user
/// is all of it once nobody else has a claim on it. Unifying them would break
/// one caller or the other — see <c>PoolDetailDto.OwnerFraction</c>.
/// </para>
/// </param>
internal sealed record PoolPosition(
    Guid ParticipantId,
    string Name,
    bool IsOwner,
    bool IsArchived,
    decimal Units,
    decimal CapitalBase,
    decimal? Stake,
    decimal? Distributable,
    decimal OwnedFraction);

/// <summary>
/// One unit event reduced to the only six facts the ownership curve needs.
/// <para>
/// Deliberately NOT a <see cref="PoolUnitEvent"/>: <see cref="OwnershipCurve"/>
/// is fed by a flat projection across pools ⋈ participants ⋈ events, and
/// materializing entities just to read six columns would defeat the point of
/// that single query. <see cref="ParticipantIsOwner"/> comes from the joined
/// participant row, which is why it is carried here rather than looked up.
/// </para>
/// </summary>
/// <param name="OccurredOn">
/// The day the units moved — the CLOSE date for a distribution, which is not
/// when its cash left. See <see cref="PoolUnitMovement.EffectiveOn"/>.
/// </param>
/// <param name="EventId">Tiebreaks same-date movements into recording order (UUIDv7).</param>
/// <param name="ParticipantIsOwner">Whether the units moved on the USER's row.</param>
/// <param name="Kind">Carries the direction; see <see cref="PoolUnitEvent.KindIncreasesUnits"/>.</param>
/// <param name="Units">Positive magnitude.</param>
/// <param name="SettledOn">
/// The day the cash physically moved; <c>null</c> only on a closed-but-unpaid
/// distribution. Carried because it — not <paramref name="OccurredOn"/> — is
/// when a distribution changes who owns the account. Still a DATE, so the
/// source's no-balance/no-FX contract is untouched.
/// </param>
internal readonly record struct PoolUnitMovement(
    DateOnly OccurredOn,
    Guid EventId,
    bool ParticipantIsOwner,
    PoolUnitEventKind Kind,
    decimal Units,
    DateOnly? SettledOn)
{
    /// <summary>
    /// The day this movement changes who owns the account: the day its CASH
    /// moved. <see cref="OccurredOn"/> for every kind except a distribution,
    /// whose money leaves at <see cref="SettledOn"/> — generally days after the
    /// close it is dated at.
    /// <para>
    /// <c>null</c> on a distribution that has closed but not been paid, which
    /// keeps its units outstanding. See <see cref="PoolUnitRegister.OwnershipCurve"/>
    /// for why that is the only correct reading against a RAW account balance.
    /// </para>
    /// </summary>
    public DateOnly? EffectiveOn =>
        Kind == PoolUnitEventKind.Distribution ? SettledOn : OccurredOn;
}

/// <summary>
/// One pool's ownership fold, readable at two granularities: the DATED step
/// function net worth consumes, and the per-movement steps behind it.
/// <para>
/// <b>Why the second reading has to exist.</b> A re-pricing mark is struck at an
/// INSTANT — the write handlers mark the account and then price units against
/// the marked balance, in one <c>SaveChangesAsync</c> — while
/// <see cref="OwnedFractionPoint"/> carries one END-OF-DAY fraction per date.
/// While a date holds at most one capital event those two agree and "the day
/// before" is a perfectly good stand-in for "just before this mark". Put a
/// subscription and a LATER mark on one calendar day and it stops being one: the
/// mark gets the pre-subscription fraction and the owner is credited with a
/// slice of a gain the friend's money was already sharing. That was a live
/// defect (2026-09-07, +208.00 USD credited whole instead of 1092/2092 of it).
/// </para>
/// <para>
/// Both readings come off the same fold on purpose. A second "just compute the
/// fraction inline here" copy is how the Performance card and the dashboard
/// would end up disagreeing about who owns an account.
/// </para>
/// </summary>
internal sealed class PoolOwnershipTimeline
{
    private readonly IReadOnlyList<OwnedFractionStep> _steps;

    internal PoolOwnershipTimeline(IReadOnlyList<OwnedFractionStep> steps, IReadOnlyList<OwnedFractionPoint> curve)
    {
        _steps = steps;
        Curve = curve;
    }

    /// <summary>
    /// One point per distinct effective date, oldest first — exactly what
    /// <see cref="AccountOwnership"/> is contracted to receive.
    /// </summary>
    public IReadOnlyList<OwnedFractionPoint> Curve { get; }

    /// <summary>
    /// The owner's share at the instant <b>immediately before</b> the movement
    /// recorded as <paramref name="eventId"/> on <paramref name="on"/> — i.e.
    /// after every movement effective on an earlier day, and after every
    /// movement effective that same day that was recorded before it.
    /// <para>
    /// <paramref name="on"/> is the day the mark being attributed is DATED,
    /// which for a distribution is its close date rather than the settlement
    /// date its units retire on. Comparing <c>(on, eventId)</c> against the
    /// step's own <c>(EffectiveOn, EventId)</c> therefore handles both cases with
    /// one rule: a closed-but-unpaid distribution simply is not in the fold yet,
    /// and a subscription recorded earlier the same day is.
    /// </para>
    /// <para>
    /// Ordering within a day is by UUIDv7 id — the same stand-in for a write
    /// clock the rest of the slice uses, and reliable for the only comparison
    /// made here: between events recorded by SEPARATE commands, which a
    /// single-user app separates by human interaction time. It is never used to
    /// order a mark against the event it prices; that pairing comes from the
    /// recorded pre-money instead (see <see cref="PoolMarkLedger.Claim"/>).
    /// </para>
    /// </summary>
    public decimal FractionBefore(DateOnly on, Guid eventId)
    {
        decimal fraction = 1m;

        foreach (OwnedFractionStep step in _steps)
        {
            // Steps are sorted by (EffectiveOn, EventId), so the first one at or
            // past the cutoff means every remaining one is too.
            if (step.On > on || step.On == on && step.EventId.CompareTo(eventId) >= 0)
            {
                break;
            }

            fraction = step.Fraction;
        }

        return fraction;
    }

    /// <summary>
    /// The owner's share after every movement effective on or before
    /// <paramref name="on"/> — identical to
    /// <see cref="AccountOwnership.OwnedFractionAsOf"/> over <see cref="Curve"/>,
    /// including its "1.0 before the first point" default. This is what a mark no
    /// event claimed gets: nothing was recorded after it that day, so the day's
    /// closing state IS the state it was struck at.
    /// </summary>
    public decimal FractionAtEndOf(DateOnly on)
    {
        decimal fraction = 1m;

        foreach (OwnedFractionStep step in _steps)
        {
            if (step.On > on)
            {
                break;
            }

            fraction = step.Fraction;
        }

        return fraction;
    }
}

/// <summary>
/// The owner's share immediately AFTER one movement took effect.
/// </summary>
/// <param name="On">The movement's effective date (<see cref="PoolUnitMovement.EffectiveOn"/>).</param>
/// <param name="EventId">The unit event, UUIDv7, standing in for the write clock.</param>
/// <param name="Fraction">The owner's share once this movement is folded in.</param>
internal readonly record struct OwnedFractionStep(DateOnly On, Guid EventId, decimal Fraction);
