using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.SavingsGoals;
using MoneyManagement.Application.Features.SavingsGoals.ArchiveGoal;
using MoneyManagement.Application.Features.SavingsGoals.CreateGoal;
using MoneyManagement.Application.Features.SavingsGoals.GetGoalDetail;
using MoneyManagement.Application.Features.SavingsGoals.GetGoals;
using MoneyManagement.Application.Features.SavingsGoals.UpdateGoal;
using MoneyManagement.Application.Features.SavingsGoals.UpdateManualSaved;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.SavingsGoals;

public sealed class GoalEndpoints : IEndpoint
{
    public sealed record CreateGoalRequest(
        string Name,
        decimal TargetAmount,
        DateOnly? TargetDate,
        Guid? LinkedAccountId);

    public sealed record UpdateGoalRequest(
        string Name,
        decimal TargetAmount,
        DateOnly? TargetDate,
        Guid? LinkedAccountId);

    public sealed record UpdateManualSavedRequest(decimal Amount);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/goals").WithTags("SavingsGoals");

        group.MapPost("/", CreateGoal);
        group.MapGet("/", GetGoals);
        group.MapGet("/{id:guid}", GetGoalDetail);
        group.MapPut("/{id:guid}", UpdateGoal);
        group.MapPatch("/{id:guid}/manual-saved", UpdateManualSaved);
        group.MapDelete("/{id:guid}", ArchiveGoal);
    }

    private static async Task<IResult> CreateGoal(
        CreateGoalRequest request,
        ICommandHandler<CreateGoalCommand, CreateGoalResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateGoalCommand(
            request.Name,
            request.TargetAmount,
            request.TargetDate,
            request.LinkedAccountId);

        Result<CreateGoalResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/goals/{response.Id}", new { id = response.Id }));
    }

    private static async Task<IResult> GetGoals(
        IQueryHandler<GetGoalsQuery, IReadOnlyList<GoalDto>> handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetGoalDetail(
        Guid id,
        IQueryHandler<GetGoalDetailQuery, GoalDetailDto> handler,
        CancellationToken cancellationToken)
    {
        Result<GoalDetailDto> result = await handler.Handle(new GetGoalDetailQuery(id), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> UpdateGoal(
        Guid id,
        UpdateGoalRequest request,
        ICommandHandler<UpdateGoalCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateGoalCommand(
            id,
            request.Name,
            request.TargetAmount,
            request.TargetDate,
            request.LinkedAccountId);

        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> UpdateManualSaved(
        Guid id,
        UpdateManualSavedRequest request,
        ICommandHandler<UpdateManualSavedCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(
            new UpdateManualSavedCommand(id, request.Amount),
            cancellationToken);

        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> ArchiveGoal(
        Guid id,
        ICommandHandler<ArchiveGoalCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new ArchiveGoalCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }
}
