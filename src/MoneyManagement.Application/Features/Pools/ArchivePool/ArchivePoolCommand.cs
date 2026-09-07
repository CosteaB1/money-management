using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.ArchivePool;

/// <summary>
/// Hides a wound-up pool. <b>Rejected while any non-owner participant still
/// holds units</b> — archiving reverts the account's owner fraction to 1.0, so
/// doing it with a friend's stake still outstanding silently reabsorbs their
/// money into the user's net worth, with no event and no audit trail.
/// <para>
/// Idempotent: archiving an already-archived pool is a no-op.
/// </para>
/// </summary>
public sealed record ArchivePoolCommand(Guid Id) : ICommand;
