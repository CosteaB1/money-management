using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.AddPoolParticipant;

/// <summary>
/// Adds a NON-owner participant to a pool. They start with zero units and
/// therefore a zero share, which is harmless: a participant only becomes an
/// owner of value once a subscription mints units for them.
/// <para>
/// There is deliberately no <c>isOwner</c> flag. The owner row is created
/// atomically with the pool and pinned by a filtered unique index; a second one
/// would double-count the user's units and silently inflate their share of the
/// account.
/// </para>
/// </summary>
/// <param name="PoolId">The pool to join.</param>
/// <param name="Name">Display name.</param>
/// <param name="JoinedOn">The day they joined. Not in the future, not before inception.</param>
public sealed record AddPoolParticipantCommand(
    Guid PoolId,
    string Name,
    DateOnly JoinedOn) : ICommand<AddPoolParticipantResponse>;

public sealed record AddPoolParticipantResponse(Guid Id);
