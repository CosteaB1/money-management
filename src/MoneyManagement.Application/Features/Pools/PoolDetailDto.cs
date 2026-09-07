using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Drill-down projection for a single pool. Carries the same valuation surface
/// as <see cref="PoolDto"/> plus the roster with per-participant economics, the
/// full ledger newest-first, the mark-staleness warning and the reconciliation
/// tripwire.
/// <para>
/// Reachable for ARCHIVED pools — the handler loads with
/// <c>IgnoreQueryFilters()</c>, the same rule the loan and savings-goal detail
/// pages follow.
/// </para>
/// </summary>
/// <param name="AsOf">The date every figure below is evaluated at (today, end of day).</param>
/// <param name="OwnerParticipantId">The user's own row. Null only on a corrupt pool.</param>
/// <param name="OwnerFraction">
/// The user's share of the pool, in <c>[0, 1]</c> — same contract as
/// <see cref="PoolDto.OwnerFraction"/>, including why it can differ from the
/// dashboard's fraction while a payout is unpaid.
/// <para>
/// <b>A wound-down pool with no units left reads 1.0, while the owner's own
/// <see cref="PoolParticipantDto.OwnershipPercent"/> in the same response reads
/// 0.</b> That is not an inconsistency to be tidied up: this field answers "how
/// much of the ACCOUNT is the user's?" — all of it, once nobody else has a
/// claim on it, which is what the net-worth seam has to hear — while the
/// participant row answers "what share of the pool does this holder have?", and
/// a share of nothing is zero. Collapsing them onto one convention breaks one
/// caller or the other.
/// </para>
/// </param>
/// <param name="LastMarkDate">
/// <b>The last time the pool's value was CONFIRMED</b> — the later of the most
/// recent re-pricing <c>IsAdjustment</c> row dated within the pool's life, and
/// the most recent non-<c>Seed</c> unit event. <c>null</c> when neither exists.
/// <para>
/// Deliberately not "the newest <c>IsAdjustment</c> row". A mark that agrees
/// with what the app already derived writes NO row (a zero delta is skipped,
/// not an error), so a stablecoin-parked pool priced on schedule would report
/// itself weeks stale while being confirmed every month. Every non-seed unit
/// event made the user type the pre-money total, which is the same
/// confirmation. Adjustments before inception belong to the account's life, not
/// the pool's, and are ignored.
/// </para>
/// <para>
/// <b>Surfaced because a NAV struck against a stale mark is the main way this
/// model goes quietly wrong.</b> Every subscription, redemption and distribution
/// is priced off the account's derived balance; if that balance is weeks old in
/// an account that moves, the units are minted or burned at the wrong price and
/// the error is permanent — it silently transfers value between the user and
/// the friends.
/// </para>
/// </param>
/// <param name="MarkAgeDays">Days between <see cref="LastMarkDate"/> and <see cref="AsOf"/>.</param>
/// <param name="Participants">The roster, owner first. Archived participants included — they are part of Σ units.</param>
/// <param name="Events">The full ledger, NEWEST first.</param>
/// <param name="Reconciliation">The replay-against-reality tripwire. Reports, never corrects.</param>
public sealed record PoolDetailDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string AccountCurrency,
    bool AccountIsArchived,
    string Name,
    string Currency,
    DateOnly InceptionDate,
    string? Notes,
    bool IsArchived,
    DateTime CreatedOn,
    DateOnly AsOf,
    decimal AccountBalance,
    decimal UnpaidDistributionCash,
    int UnpaidDistributionCount,
    decimal PoolValue,
    decimal? PoolValueMdl,
    decimal TotalUnits,
    decimal? NavPerUnit,
    Guid? OwnerParticipantId,
    decimal OwnerFraction,
    decimal OutsideCapital,
    decimal? OutsideCapitalMdl,
    bool MissingFxRate,
    DateOnly? LastMarkDate,
    int? MarkAgeDays,
    IReadOnlyList<PoolParticipantDto> Participants,
    IReadOnlyList<PoolUnitEventDto> Events,
    PoolReconciliationDto Reconciliation);

/// <summary>One holder's position in the pool, valued at <see cref="PoolDetailDto.AsOf"/>.</summary>
/// <param name="Units">Σ of their signed unit deltas.</param>
/// <param name="OwnershipPercent">
/// <c>Units ÷ TotalUnits × 100</c>, to 6dp. Unit counts only; NAV never enters
/// it.
/// <para>
/// <b>ZERO when the pool holds no units</b>, where
/// <see cref="PoolDetailDto.OwnerFraction"/> in the same response reads 1.0.
/// Both are deliberate and neither is the other's bug — see that field.
/// </para>
/// </param>
/// <param name="Stake"><c>Units × NavPerUnit</c>, to the cent — <c>null</c> when NAV is undefined.</param>
/// <param name="StakeMdl">Reporting-currency value of <see cref="Stake"/> at today's rate.</param>
/// <param name="CapitalBase">
/// <c>Σ subscription cash − Σ redemption cash</c>. Distributions and cost events
/// never move it, which IS the high-water mark — no stored field, no reset
/// logic.
/// </param>
/// <param name="Distributable">
/// <c>max(0, Stake − max(0, CapitalBase))</c>, floored to the cent and capped at
/// <see cref="Stake"/>. Zero below basis, so a green month after a drawdown
/// correctly pays nothing, and a participant who skips a month simply leaves it
/// accumulating.
/// <para>
/// The base is floored at zero <b>for this figure only</b> —
/// <see cref="CapitalBase"/> keeps its signed definition. Somebody who has
/// withdrawn more cash than they subscribed has already recovered their
/// capital, so all that is left is profit; without the floor the payable would
/// exceed the whole stake and a close would try to retire units nobody holds.
/// </para>
/// <para>
/// <b>Read the OWNER's value with care.</b> A seed carries no cash, so the
/// owner's capital base is 0 and their "distributable" is their entire stake.
/// That is the formula being honest, not an amount to offer a button for: the
/// owner's profit stays in and grows their share, and they take value out with a
/// redemption. The default payout set excludes the owner for exactly this
/// reason.
/// </para>
/// </param>
/// <param name="UnpaidDistributionCash">Closed-but-unpaid payouts owed to this participant.</param>
/// <param name="UnpaidDistributionCount">How many of their distribution events are still owed.</param>
/// <param name="MissingFxRate">True when <see cref="StakeMdl"/> could not be valued.</param>
public sealed record PoolParticipantDto(
    Guid Id,
    string Name,
    bool IsOwner,
    bool IsArchived,
    DateOnly JoinedOn,
    decimal Units,
    decimal OwnershipPercent,
    decimal? Stake,
    decimal? StakeMdl,
    decimal CapitalBase,
    decimal? Distributable,
    decimal UnpaidDistributionCash,
    int UnpaidDistributionCount,
    bool MissingFxRate);

/// <summary>One row of the pool's ledger.</summary>
/// <param name="Units">Positive magnitude; <see cref="Kind"/> carries the direction.</param>
/// <param name="UnitsDelta">The signed effect on the participant's balance.</param>
/// <param name="NavPerUnit">The price the units were struck at. AUDIT ONLY — it never enters the ownership fraction.</param>
/// <param name="PoolValuePreMoney">The pool's total value immediately before this event. Zero for a seed.</param>
/// <param name="Cash">Money that actually moved; <c>null</c> for Seed, CostShare and CostRecovery, which move units only.</param>
/// <param name="SettledOn">
/// The day the cash physically moved. <c>null</c> on an unpaid distribution —
/// and only there.
/// </param>
/// <param name="IsUnpaid">A distribution that has closed but not yet been paid out.</param>
/// <param name="MovementTransactionId">The synthesized money row, when there is one.</param>
/// <param name="MovementAccountId">Resolved from the transaction; <c>null</c> when the link dangles.</param>
/// <param name="MovementAccountName">Likewise; an archived account still labels its movement.</param>
public sealed record PoolUnitEventDto(
    Guid Id,
    Guid ParticipantId,
    string ParticipantName,
    PoolUnitEventKind Kind,
    DateOnly OccurredOn,
    decimal Units,
    decimal UnitsDelta,
    decimal NavPerUnit,
    decimal PoolValuePreMoney,
    decimal? Cash,
    string? CashCurrency,
    DateOnly? SettledOn,
    bool IsUnpaid,
    Guid? MovementTransactionId,
    Guid? MovementAccountId,
    string? MovementAccountName,
    string? Notes);

/// <summary>
/// The pool's ledger replayed against reality. <b>Reports; never corrects, never
/// throws.</b> Everything here is recoverable by hand and unrecoverable if
/// silently "fixed" — a value drift means somebody edited history after units
/// were priced, and the right answer depends on which of the two records is
/// wrong.
/// </summary>
/// <param name="IsClean">True when all four checks pass. The only field a badge needs.</param>
/// <param name="UnmatchedTransactions">
/// Money that moved on the pool account without a unit event to account for it.
/// <b>Each one is silently shared pro-rata with the outside investors</b>, which
/// is why the write slice guards every path that can produce one; a row here
/// means a guard was bypassed or the row predates the guards.
/// </param>
/// <param name="ParticipantUnits">Σ units across the roster.</param>
/// <param name="LedgerUnits">Σ unit deltas across the ledger.</param>
/// <param name="UnitsDrift"><c>ParticipantUnits − LedgerUnits</c>.</param>
/// <param name="UnitsBalance">
/// Whether the two agree to within dust. Assertable ONLY because the owner holds
/// real units rather than a residual — modelled as "whatever is left over" this
/// would be true by construction and worth nothing.
/// </param>
/// <param name="ValueDrifts">
/// Events whose recorded <c>PoolValuePreMoney</c> no longer matches the balance
/// re-derived as of that event. Someone moved, added or deleted a row dated on
/// or before a date units had already been priced at.
/// </param>
/// <param name="UnbackedCashClaims">
/// The mirror of <see cref="UnmatchedTransactions"/>: unit events that say cash
/// moved on a date the account has no row for. <b>Units were minted or burned
/// against money that never landed</b>, which mis-prices every participant.
/// <para>
/// This is the only check that can catch a phantom backfill. The pre-money
/// replay skips back-dated events on purpose — a market move between inception
/// and a friend's arrival has no row anywhere — so a <c>CreatePool</c> backfill
/// claiming an arrival that never happened clears every other check.
/// </para>
/// </param>
public sealed record PoolReconciliationDto(
    bool IsClean,
    IReadOnlyList<UnmatchedPoolTransactionDto> UnmatchedTransactions,
    decimal ParticipantUnits,
    decimal LedgerUnits,
    decimal UnitsDrift,
    bool UnitsBalance,
    IReadOnlyList<PoolValueDriftDto> ValueDrifts,
    IReadOnlyList<UnbackedPoolCashClaimDto> UnbackedCashClaims);

/// <summary>A transaction on the pool account with no unit event behind it.</summary>
public sealed record UnmatchedPoolTransactionDto(
    Guid TransactionId,
    DateOnly TransactionDate,
    string Description,
    TransactionDirection Direction,
    decimal Amount,
    string Currency,
    bool IsTransfer);

/// <summary>
/// A unit event claiming cash the account never saw: no non-deleted row on the
/// pool account matches its settlement date, direction and amount.
/// </summary>
/// <param name="EventId">The unit event making the claim.</param>
/// <param name="SettledOn">The day the event says the money moved.</param>
/// <param name="Direction">Which way it says the money went.</param>
/// <param name="Amount">How much, in the pool's currency.</param>
public sealed record UnbackedPoolCashClaimDto(
    Guid EventId,
    DateOnly SettledOn,
    TransactionDirection Direction,
    decimal Amount);

/// <summary>An event priced against a pool value the ledger can no longer reproduce.</summary>
/// <param name="RecordedPreMoney">What the event says the pool was worth immediately before it.</param>
/// <param name="DerivedPreMoney">What replaying the account's rows says it was worth.</param>
/// <param name="Drift"><c>RecordedPreMoney − DerivedPreMoney</c>.</param>
public sealed record PoolValueDriftDto(
    Guid EventId,
    DateOnly OccurredOn,
    PoolUnitEventKind Kind,
    decimal RecordedPreMoney,
    decimal DerivedPreMoney,
    decimal Drift);
