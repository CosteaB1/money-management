using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.RecordRedemption;

/// <summary>
/// Money out. Burns units at the prevailing NAV, which leaves NAV mathematically
/// unchanged: <c>(V − X) ÷ (U − X/nav) = nav</c>. An owner withdrawal and a
/// friend's exit are literally the same operation.
/// <para>
/// <b>Dated today, always</b> — same reasoning as a subscription: the mark is
/// measured against the live balance.
/// </para>
/// </summary>
/// <param name="PoolId">The pool.</param>
/// <param name="ParticipantId">Whose units are being burned.</param>
/// <param name="PoolValueNow">The exchange's REAL total right now, before the money leaves. Marked first.</param>
/// <param name="Cash">The amount leaving, in the pool's currency.</param>
/// <param name="DestinationAccountId">
/// <b>Set this whenever the money lands in another tracked account.</b> The
/// owner's Bybit withdrawal takes this two-leg path: two reciprocal
/// transfer-flagged rows, exactly as <c>CreateTransferCommandHandler</c> writes
/// them. Leaving it null writes a single Expense leg with no counter account —
/// correct only when the money genuinely leaves the tracked world (a friend
/// cashing out to their own wallet). Getting that backwards on a live transfer
/// silently deletes real net worth.
/// <para>
/// <b>OWNER ONLY</b> (<c>pools.destination_requires_owner</c>). The counter leg
/// lands on a wholly-owned account, so naming one for a NON-owner would book a
/// friend's payout as the user's own money — see the handler's
/// <c>ResolveDestinationAsync</c>.
/// </para>
/// </param>
/// <param name="Notes">Free text, carried onto the unit event and both money rows.</param>
/// <param name="IsFullWindDown">
/// <b>"Yes, this really is the whole pool."</b> Opt-in acknowledgement that
/// relaxes <c>pools.cash_looks_like_a_balance</c> for this one command.
/// <para>
/// That guard rejects cash equal to the account's balance because the
/// demonstrated slip is a BALANCE typed into an AMOUNT field — but the last
/// redemption of a pool IS the whole pool value, so winding one down ("I moved
/// everything to Bybit and I'm done") otherwise has no path through the API at
/// all: repeated partial redemptions converge on zero without ever reaching it.
/// </para>
/// <para>
/// <b>Not a bypass switch.</b> The flag is an assertion about the ledger and the
/// handler refutes it: if any units would remain outstanding afterwards the
/// command fails with <c>pools.not_a_full_wind_down</c>. So the typo the guard
/// was built to catch still cannot get through — it would have to happen to
/// retire every unit in the pool, which is precisely the case being allowed.
/// </para>
/// </param>
public sealed record RecordRedemptionCommand(
    Guid PoolId,
    Guid ParticipantId,
    decimal PoolValueNow,
    decimal Cash,
    Guid? DestinationAccountId,
    string? Notes,
    bool IsFullWindDown = false) : ICommand<RecordRedemptionResponse>;

/// <param name="EventId">The unit event.</param>
/// <param name="Units">Units burned.</param>
/// <param name="NavPerUnit">The price they were burned at — unchanged by this operation.</param>
/// <param name="PoolValuePreMoney">The pool's value immediately before the money left.</param>
/// <param name="MarkDelta">Signed catch-up applied to the account, 0 when the app already agreed.</param>
/// <param name="MarkTransactionId">The mark row, when one was needed.</param>
/// <param name="MovementTransactionId">The Expense leg on the pooled account.</param>
/// <param name="CounterTransactionId">The Income leg on the destination account, when there is one.</param>
public sealed record RecordRedemptionResponse(
    Guid EventId,
    decimal Units,
    decimal NavPerUnit,
    decimal PoolValuePreMoney,
    decimal MarkDelta,
    Guid? MarkTransactionId,
    Guid MovementTransactionId,
    Guid? CounterTransactionId);
