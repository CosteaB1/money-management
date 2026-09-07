using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.CloseDistribution;

/// <summary>
/// Phase one of a payout: <b>CLOSE ONLY — no cash moves.</b> The pool is marked,
/// units are redeemed at that NAV, and the amount owed is recorded with
/// <c>SettledOn = null</c> and no transaction. Phase two
/// (<c>POST /pools/{id}/distributions/{eventId}/settle</c>) writes the real
/// transfer on the day the money actually leaves.
/// <para>
/// The split exists because the month closes at month-end but the USDT
/// physically leaves at the start of the next month. Date the payout at the
/// close and send it days later and the next snapshot still contains that cash,
/// which the app then re-attributes as fresh profit — the friends get paid twice
/// on the same money, every month. Between the two phases
/// <c>PoolUnitRegister</c> subtracts unpaid distributions from pool value, which
/// is what keeps NAV honest in the gap.
/// </para>
/// </summary>
/// <param name="PoolId">The pool being closed.</param>
/// <param name="PoolValueNow">
/// The exchange's REAL total at the close, INCLUDING the BNB balance. Marked
/// first, then the NAV is struck against it. Either BNB convention nets out over
/// time; switching between them manufactures phantom profit.
/// </param>
/// <param name="Payouts">
/// Who gets paid and how much. <c>null</c> or empty means <b>every non-owner
/// participant with distributable profit, in full</b> — the ordinary monthly
/// close.
/// <para>
/// Naming a subset is how reinvestment works: a participant left off the list
/// simply keeps their units, so their distributable keeps accumulating and they
/// take it whenever they like. There is no reinvestment flag and no stored base
/// to bump — omitting them IS the feature.
/// </para>
/// </param>
/// <param name="Notes">Free text, recorded on every unit event this close writes.</param>
public sealed record CloseDistributionCommand(
    Guid PoolId,
    decimal PoolValueNow,
    IReadOnlyList<DistributionPayout>? Payouts,
    string? Notes) : ICommand<CloseDistributionResponse>;

/// <param name="ParticipantId">Who is being paid.</param>
/// <param name="Cash">
/// How much. Omit for their full distributable, <c>max(0, stake − capitalBase)</c>.
/// May never exceed it: capital base is untouched by distributions, so paying
/// above it would hand back principal and quietly reset the high-water mark.
/// </param>
public sealed record DistributionPayout(Guid ParticipantId, decimal? Cash = null);

/// <param name="NavPerUnit">The single price every line was struck at — unchanged by the close.</param>
/// <param name="PoolValuePreMoney">The pool's value at the close, before any units were burned.</param>
/// <param name="MarkDelta">Signed catch-up applied to the account.</param>
/// <param name="MarkTransactionId">The mark row, when one was needed.</param>
/// <param name="TotalCash">Sum of every line — owed, not yet paid.</param>
/// <param name="Lines">One per participant paid.</param>
public sealed record CloseDistributionResponse(
    decimal NavPerUnit,
    decimal PoolValuePreMoney,
    decimal MarkDelta,
    Guid? MarkTransactionId,
    decimal TotalCash,
    IReadOnlyList<DistributionLine> Lines);

/// <param name="EventId">The unit event, and the id the settle call needs.</param>
/// <param name="ParticipantId">Who it is owed to.</param>
/// <param name="ParticipantName">Their display name at close time.</param>
/// <param name="Units">Units burned.</param>
/// <param name="Cash">Cash owed.</param>
public sealed record DistributionLine(
    Guid EventId,
    Guid ParticipantId,
    string ParticipantName,
    decimal Units,
    decimal Cash);
