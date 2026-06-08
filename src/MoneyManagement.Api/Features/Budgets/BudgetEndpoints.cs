using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Budgets;
using MoneyManagement.Application.Features.Budgets.ArchiveBudget;
using MoneyManagement.Application.Features.Budgets.CreateBudget;
using MoneyManagement.Application.Features.Budgets.GetBudgets;
using MoneyManagement.Application.Features.Budgets.RebuildBudgetPeriods;
using MoneyManagement.Application.Features.Budgets.UpdateBudgetLimit;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Budgets;

public sealed class BudgetEndpoints : IEndpoint
{
    public sealed record CreateBudgetRequest(Guid CategoryId, decimal MonthlyLimit);

    public sealed record UpdateBudgetLimitRequest(decimal MonthlyLimit);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/budgets").WithTags("Budgets");

        group.MapPost("/", CreateBudget);
        group.MapGet("/", GetBudgets);
        group.MapPut("/{id:guid}", UpdateBudgetLimit);
        group.MapDelete("/{id:guid}", ArchiveBudget);
        group.MapPost("/{id:guid}/rebuild-periods", RebuildSingle);
        group.MapPost("/rebuild-all-periods", RebuildAll);
    }

    private static async Task<IResult> CreateBudget(
        CreateBudgetRequest request,
        ICommandHandler<CreateBudgetCommand, CreateBudgetResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateBudgetCommand(request.CategoryId, request.MonthlyLimit);
        Result<CreateBudgetResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/budgets/{response.Id}", new { id = response.Id }));
    }

    private static async Task<IResult> GetBudgets(
        IQueryHandler<GetBudgetsQuery, IReadOnlyList<BudgetDto>> handler,
        CancellationToken cancellationToken,
        int? year = null,
        int? month = null)
    {
        Result<IReadOnlyList<BudgetDto>> result = await handler.Handle(
            new GetBudgetsQuery(year, month),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> UpdateBudgetLimit(
        Guid id,
        UpdateBudgetLimitRequest request,
        ICommandHandler<UpdateBudgetLimitCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(
            new UpdateBudgetLimitCommand(id, request.MonthlyLimit),
            cancellationToken);

        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> ArchiveBudget(
        Guid id,
        ICommandHandler<ArchiveBudgetCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new ArchiveBudgetCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> RebuildSingle(
        Guid id,
        ICommandHandler<RebuildBudgetPeriodsCommand, RebuildBudgetPeriodsResult> handler,
        CancellationToken cancellationToken)
    {
        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(id),
            cancellationToken);

        return result.Match(r => Results.Ok(new { budgetsRebuilt = r.BudgetsRebuilt, periodsAffected = r.PeriodsAffected }));
    }

    private static async Task<IResult> RebuildAll(
        ICommandHandler<RebuildBudgetPeriodsCommand, RebuildBudgetPeriodsResult> handler,
        CancellationToken cancellationToken)
    {
        Result<RebuildBudgetPeriodsResult> result = await handler.Handle(
            new RebuildBudgetPeriodsCommand(BudgetId: null),
            cancellationToken);

        return result.Match(r => Results.Ok(new { budgetsRebuilt = r.BudgetsRebuilt, periodsAffected = r.PeriodsAffected }));
    }
}
