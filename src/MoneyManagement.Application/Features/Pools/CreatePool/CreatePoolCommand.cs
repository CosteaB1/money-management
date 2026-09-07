using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.CreatePool;

/// <summary>
/// Creates a pool, its owner participant and the owner's seed — atomically, in
/// one <c>SaveChangesAsync</c>. A pool without an owner or without a seed has no
/// units, so its NAV is undefined and every later command fails; the three are
/// therefore never separate calls.
/// </summary>
/// <param name="AccountId">
/// The account the pool's capital physically sits in. Explicit and immutable:
/// it is never inferred from the account's type, because a second
/// CryptoExchange/USD account (Bybit) looks identical and guessing would
/// silently pool an account nobody else has money in.
/// </param>
/// <param name="Name">Display name, e.g. "Binance pool".</param>
/// <param name="Currency">
/// The pool's denomination. Must equal the account's — the pool never does FX.
/// Passed explicitly rather than derived so a client that believes the account
/// is USD gets a clear rejection instead of a silently re-denominated pool.
/// </param>
/// <param name="InceptionDate">The day the pool starts. The seed is dated here.</param>
/// <param name="OwnerName">Display name for the user's own participant row, e.g. "Me".</param>
/// <param name="PoolValueAtInception">
/// The account's TRUE total on <paramref name="InceptionDate"/>. The handler
/// writes a catch-up mark for the difference against the derived balance, then
/// seeds that many units at a NAV of exactly 1. Omit to seed against the balance
/// the app already derives (no mark).
/// </param>
/// <param name="Notes">Free text, optional.</param>
/// <param name="BackdatedSubscriptions">
/// Friends who already paid in before the pool existed. <b>This is the only
/// place in the slice where back-dating is permitted</b> — every other command
/// prices as of today, because the adjustment delta elsewhere is computed
/// against a date-blind sum. Creation is safe because it prices against
/// <c>AccountBalanceLedger</c>'s as-of balance and there are no prior unit
/// events to re-price.
/// </param>
public sealed record CreatePoolCommand(
    Guid AccountId,
    string Name,
    string Currency,
    DateOnly InceptionDate,
    string OwnerName,
    decimal? PoolValueAtInception,
    string? Notes,
    IReadOnlyList<BackdatedSubscription>? BackdatedSubscriptions) : ICommand<CreatePoolResponse>;

/// <summary>
/// One friend's already-completed subscription, replayed into the ledger at the
/// NAV that applied on the day their money actually arrived.
/// </summary>
/// <param name="ParticipantName">Their display name; a participant row is created for them.</param>
/// <param name="OccurredOn">The day their money arrived. Between inception and today.</param>
/// <param name="Cash">What they put in, in the pool's currency.</param>
/// <param name="PoolValuePreMoney">
/// The pool's total value IMMEDIATELY BEFORE their money landed. Required, and
/// deliberately not defaulted: the account's derived balance on that date
/// already contains their cash, so guessing would price them at their own
/// subscription and hand the difference to everyone else.
/// </param>
/// <param name="WriteMovementTransaction">
/// <b>Defaults to false, and that default is load-bearing.</b> "Already paid in"
/// normally means the arrival is already recorded on the account, so
/// synthesizing an Income leg here would double the account — the same trap the
/// seed carve-out exists for. Set true only when the money landed but was never
/// entered.
/// </param>
/// <param name="Notes">Free text, optional.</param>
public sealed record BackdatedSubscription(
    string ParticipantName,
    DateOnly OccurredOn,
    decimal Cash,
    decimal PoolValuePreMoney,
    bool WriteMovementTransaction = false,
    string? Notes = null);

/// <param name="Id">The new pool.</param>
/// <param name="OwnerParticipantId">The owner row, needed for every later owner redemption.</param>
/// <param name="SeedUnits">Units minted to the owner at a NAV of 1.</param>
/// <param name="MarkDelta">Signed catch-up applied to the account, or 0 when none was needed.</param>
/// <param name="MarkTransactionId">The catch-up mark row, when one was written.</param>
/// <param name="Participants">Everyone created, owner first, with their opening unit balance.</param>
public sealed record CreatePoolResponse(
    Guid Id,
    Guid OwnerParticipantId,
    decimal SeedUnits,
    decimal MarkDelta,
    Guid? MarkTransactionId,
    IReadOnlyList<CreatedPoolParticipant> Participants);

/// <param name="Id">The participant row.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsOwner">Whether this is the user's own row.</param>
/// <param name="Units">Units they hold immediately after creation.</param>
public sealed record CreatedPoolParticipant(Guid Id, string Name, bool IsOwner, decimal Units);
