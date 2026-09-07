using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.RecordSubscription;

/// <summary>
/// Money in. Mints units at the prevailing NAV, which leaves NAV mathematically
/// unchanged: <c>(V + X) ÷ (U + X/nav) = nav</c>. Nobody else's per-unit value
/// moves by a cent.
/// <para>
/// <b>Dated today, always.</b> There is no date field on purpose. The mark
/// written first is measured against the account's live balance, so a
/// back-dated subscription would price against a balance that already contains
/// later movements. Back-dating exists in exactly one place — the backfill list
/// on <c>CreatePoolCommand</c>, where there are no prior unit events to
/// corrupt.
/// </para>
/// </summary>
/// <param name="PoolId">The pool being subscribed to.</param>
/// <param name="ParticipantId">Who is putting money in. Must be an active participant of this pool.</param>
/// <param name="PoolValueNow">
/// The exchange's REAL total right now, before the new money lands. The handler
/// writes the catch-up mark for the difference against the derived balance
/// FIRST and only then strikes the NAV — which is what structurally defeats the
/// ordering trap where a post-arrival snapshot books the subscription as
/// trading profit.
/// </param>
/// <param name="Cash">The amount actually being put in, in the pool's currency.</param>
/// <param name="Notes">Free text, carried onto both the unit event and the money row.</param>
public sealed record RecordSubscriptionCommand(
    Guid PoolId,
    Guid ParticipantId,
    decimal PoolValueNow,
    decimal Cash,
    string? Notes) : ICommand<RecordSubscriptionResponse>;

/// <param name="EventId">The unit event.</param>
/// <param name="Units">Units minted.</param>
/// <param name="NavPerUnit">The price they were struck at — unchanged by this operation.</param>
/// <param name="PoolValuePreMoney">The pool's value immediately before the money landed.</param>
/// <param name="MarkDelta">Signed catch-up applied to the account, 0 when the app already agreed.</param>
/// <param name="MarkTransactionId">The mark row, when one was needed.</param>
/// <param name="MovementTransactionId">The transfer-flagged Income row for the cash.</param>
public sealed record RecordSubscriptionResponse(
    Guid EventId,
    decimal Units,
    decimal NavPerUnit,
    decimal PoolValuePreMoney,
    decimal MarkDelta,
    Guid? MarkTransactionId,
    Guid MovementTransactionId);
