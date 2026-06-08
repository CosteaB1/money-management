using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Transactions.CreateTransfer;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Transfers;

public sealed class TransferEndpoints : IEndpoint
{
    public sealed record CreateTransferRequest(
        Guid SourceAccountId,
        Guid DestinationAccountId,
        decimal Amount,
        DateOnly Date,
        string Description,
        Guid? CategoryId,
        decimal? DestinationAmount = null,
        string? Notes = null);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/transfers").WithTags("Transfers");

        group.MapPost("/", CreateTransfer);
    }

    private static async Task<IResult> CreateTransfer(
        CreateTransferRequest request,
        ICommandHandler<CreateTransferCommand, TransferResult> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateTransferCommand(
            request.SourceAccountId,
            request.DestinationAccountId,
            request.Amount,
            request.Date,
            request.Description,
            request.CategoryId,
            request.DestinationAmount,
            request.Notes);

        Result<TransferResult> result = await handler.Handle(command, cancellationToken);
        return result.Match(dto => Results.Created(
            $"/transfers/{dto.SourceTransactionId}",
            new
            {
                sourceTransactionId = dto.SourceTransactionId,
                destinationTransactionId = dto.DestinationTransactionId,
            }));
    }
}
