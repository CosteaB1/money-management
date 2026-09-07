using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.AddPoolParticipant;

internal sealed class AddPoolParticipantCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : ICommandHandler<AddPoolParticipantCommand, AddPoolParticipantResponse>
{
    public async Task<Result<AddPoolParticipantResponse>> Handle(
        AddPoolParticipantCommand command,
        CancellationToken cancellationToken)
    {
        // The archive query filter hides archived pools, so joining one 404s
        // naturally; the explicit predicate is defense-in-depth for unit tests
        // that bypass model configuration.
        Pool? pool = await db.Pools
            .FirstOrDefaultAsync(p => p.Id == command.PoolId && !p.IsArchived, cancellationToken);

        if (pool is null)
        {
            return Result.Failure<AddPoolParticipantResponse>(PoolErrors.NotFound(command.PoolId));
        }

        Result<PoolParticipant> participantResult = PoolParticipant.Create(
            pool.Id,
            command.Name,
            isOwner: false,
            command.JoinedOn,
            pool.InceptionDate,
            clock);

        if (participantResult.IsFailure)
        {
            return Result.Failure<AddPoolParticipantResponse>(participantResult.Error);
        }

        PoolParticipant participant = participantResult.Value;
        db.PoolParticipants.Add(participant);
        await db.SaveChangesAsync(cancellationToken);

        return new AddPoolParticipantResponse(participant.Id);
    }
}
