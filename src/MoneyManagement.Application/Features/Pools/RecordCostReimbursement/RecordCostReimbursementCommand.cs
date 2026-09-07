using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.RecordCostReimbursement;

/// <summary>
/// The owner paid a pool cost (a server bill) out of their own pocket and the
/// other participants reimburse their share pro-rata.
/// <para>
/// <b>A PURE UNIT TRANSFER. No cash leg, no transaction, no change to pool value
/// and no change to NAV.</b> That money never entered the account, so minting
/// units for it would create value out of nothing. Instead each non-owner's
/// units fall by <c>cost × theirShare ÷ nav</c> (<c>CostShare</c>) and the
/// owner's rise by exactly that TOTAL (<c>CostRecovery</c>). Total units are
/// unchanged, so <c>navPerUnit = poolValue ÷ totalUnits</c> is unchanged too —
/// the transfer only moves who owns the pool.
/// </para>
/// <para>
/// The owner is NOT reimbursed for their own share: they recover
/// <c>cost × (1 − ownerShare)</c>, which is precisely the part of the bill that
/// was not theirs to bear.
/// </para>
/// <para>
/// <b>BNB trading fees are explicitly not recorded here.</b> They are bought
/// with pool money and spent on pool trades, so the close snapshot already
/// carries them; putting them through this command would deduct them twice.
/// </para>
/// </summary>
/// <param name="PoolId">The pool.</param>
/// <param name="Amount">The full cost the owner paid, in the pool's currency.</param>
/// <param name="PoolValueNow">
/// Optional. Supply the exchange's real total to mark first (the same
/// discipline as every other pricing operation) — a stale NAV transfers the
/// wrong number of units even though the value transferred is fixed. Omit it to
/// price at the NAV the app already derives, which is the right call right after
/// a close, when the mark would be zero anyway.
/// </param>
/// <param name="Notes">Free text, recorded on every unit event this writes.</param>
public sealed record RecordCostReimbursementCommand(
    Guid PoolId,
    decimal Amount,
    decimal? PoolValueNow,
    string? Notes) : ICommand<RecordCostReimbursementResponse>;

/// <param name="NavPerUnit">The price the transfer was struck at — unchanged by it.</param>
/// <param name="TotalUnitsTransferred">Units taken from the participants and given to the owner.</param>
/// <param name="TotalAmountRecovered">The share of the cost the participants bore.</param>
/// <param name="MarkDelta">Signed catch-up applied to the account, 0 when no value was supplied.</param>
/// <param name="MarkTransactionId">The mark row, when one was written.</param>
/// <param name="OwnerEventId">The owner's <c>CostRecovery</c> event.</param>
/// <param name="Lines">One <c>CostShare</c> per contributing participant.</param>
public sealed record RecordCostReimbursementResponse(
    decimal NavPerUnit,
    decimal TotalUnitsTransferred,
    decimal TotalAmountRecovered,
    decimal MarkDelta,
    Guid? MarkTransactionId,
    Guid OwnerEventId,
    IReadOnlyList<CostShareLine> Lines);

/// <param name="EventId">The <c>CostShare</c> unit event.</param>
/// <param name="ParticipantId">Who bore it.</param>
/// <param name="ParticipantName">Their display name.</param>
/// <param name="Units">Units taken from them.</param>
/// <param name="Amount">The value of those units — their share of the cost.</param>
public sealed record CostShareLine(
    Guid EventId,
    Guid ParticipantId,
    string ParticipantName,
    decimal Units,
    decimal Amount);
