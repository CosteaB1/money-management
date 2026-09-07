using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.AddPoolParticipant;
using MoneyManagement.Application.Features.Pools.ArchivePool;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.DeletePoolEvent;
using MoneyManagement.Application.Features.Pools.GetPoolDetail;
using MoneyManagement.Application.Features.Pools.GetPools;
using MoneyManagement.Application.Features.Pools.RecordCostReimbursement;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Pools;

/// <summary>
/// Read and write surface for pooled capital. Self-registers through
/// <see cref="IEndpoint"/>.
/// <para>
/// Every pricing route takes <c>poolValueNow</c> — the exchange's real total —
/// and the handler marks the account to it BEFORE striking the NAV. There is
/// deliberately no "value before" / "value after" pair and no event date on the
/// pricing routes: back-dating is permitted only in
/// <c>POST /pools</c>'s backfill list.
/// </para>
/// </summary>
public sealed class PoolEndpoints : IEndpoint
{
    public sealed record CreatePoolRequest(
        Guid AccountId,
        string Name,
        string Currency,
        DateOnly InceptionDate,
        string OwnerName,
        decimal? PoolValueAtInception,
        string? Notes,
        IReadOnlyList<BackdatedSubscriptionRequest>? BackdatedSubscriptions);

    public sealed record BackdatedSubscriptionRequest(
        string ParticipantName,
        DateOnly OccurredOn,
        decimal Cash,
        decimal PoolValuePreMoney,
        bool WriteMovementTransaction = false,
        string? Notes = null);

    public sealed record AddParticipantRequest(string Name, DateOnly JoinedOn);

    public sealed record SubscriptionRequest(
        Guid ParticipantId,
        decimal PoolValueNow,
        decimal Cash,
        string? Notes);

    /// <param name="IsFullWindDown">
    /// Opt-in acknowledgement that this redemption takes the pool's ENTIRE
    /// remaining value. Relaxes <c>pools.cash_looks_like_a_balance</c>, which
    /// otherwise makes a full wind-down unreachable; refused with
    /// <c>pools.not_a_full_wind_down</c> if units would remain outstanding.
    /// Defaults to <c>false</c>, so an existing client body is unchanged.
    /// </param>
    public sealed record RedemptionRequest(
        Guid ParticipantId,
        decimal PoolValueNow,
        decimal Cash,
        Guid? DestinationAccountId,
        string? Notes,
        bool IsFullWindDown = false);

    public sealed record DistributionRequest(
        decimal PoolValueNow,
        IReadOnlyList<DistributionPayoutRequest>? Payouts,
        string? Notes);

    public sealed record DistributionPayoutRequest(Guid ParticipantId, decimal? Cash);

    public sealed record SettleDistributionRequest(DateOnly? SettledOn, string? Notes);

    public sealed record CostReimbursementRequest(decimal Amount, decimal? PoolValueNow, string? Notes);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/pools").WithTags("Pools");

        group.MapGet("/", GetPools);
        group.MapGet("/{id:guid}", GetPoolDetail);
        group.MapPost("/", CreatePool);
        group.MapPost("/{id:guid}/participants", AddParticipant);
        group.MapPost("/{id:guid}/subscriptions", RecordSubscription);
        group.MapPost("/{id:guid}/redemptions", RecordRedemption);
        group.MapPost("/{id:guid}/distributions", CloseDistribution);
        group.MapPost("/{id:guid}/distributions/{eventId:guid}/settle", SettleDistribution);
        group.MapPost("/{id:guid}/cost-reimbursements", RecordCostReimbursement);
        group.MapDelete("/{id:guid}/events/{eventId:guid}", DeleteEvent);
        group.MapPost("/{id:guid}/archive", ArchivePool);
    }

    private static async Task<IResult> GetPools(
        IQueryHandler<GetPoolsQuery, IReadOnlyList<PoolDto>> handler,
        CancellationToken cancellationToken,
        bool includeArchived = false)
    {
        Result<IReadOnlyList<PoolDto>> result =
            await handler.Handle(new GetPoolsQuery(includeArchived), cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetPoolDetail(
        Guid id,
        IQueryHandler<GetPoolDetailQuery, PoolDetailDto> handler,
        CancellationToken cancellationToken)
    {
        Result<PoolDetailDto> result = await handler.Handle(new GetPoolDetailQuery(id), cancellationToken);

        // Archived pools stay drillable here on purpose (the goals/loans
        // precedent): the handler loads with IgnoreQueryFilters.
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> CreatePool(
        CreatePoolRequest request,
        ICommandHandler<CreatePoolCommand, CreatePoolResponse> handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BackdatedSubscription>? backfills = request.BackdatedSubscriptions is null
            ? null
            :
            [
                .. request.BackdatedSubscriptions.Select(s => new BackdatedSubscription(
                    s.ParticipantName,
                    s.OccurredOn,
                    s.Cash,
                    s.PoolValuePreMoney,
                    s.WriteMovementTransaction,
                    s.Notes)),
            ];

        var command = new CreatePoolCommand(
            request.AccountId,
            request.Name,
            request.Currency,
            request.InceptionDate,
            request.OwnerName,
            request.PoolValueAtInception,
            request.Notes,
            backfills);

        Result<CreatePoolResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/pools/{response.Id}", response));
    }

    private static async Task<IResult> AddParticipant(
        Guid id,
        AddParticipantRequest request,
        ICommandHandler<AddPoolParticipantCommand, AddPoolParticipantResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new AddPoolParticipantCommand(id, request.Name, request.JoinedOn);

        Result<AddPoolParticipantResponse> result = await handler.Handle(command, cancellationToken);

        // Participants have no standalone GET; the pool page is where they
        // surface, so Location points at the parent resource.
        return result.Match(response => Results.Created($"/pools/{id}", new { id = response.Id }));
    }

    private static async Task<IResult> RecordSubscription(
        Guid id,
        SubscriptionRequest request,
        ICommandHandler<RecordSubscriptionCommand, RecordSubscriptionResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new RecordSubscriptionCommand(
            id,
            request.ParticipantId,
            request.PoolValueNow,
            request.Cash,
            request.Notes);

        Result<RecordSubscriptionResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/pools/{id}", response));
    }

    private static async Task<IResult> RecordRedemption(
        Guid id,
        RedemptionRequest request,
        ICommandHandler<RecordRedemptionCommand, RecordRedemptionResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new RecordRedemptionCommand(
            id,
            request.ParticipantId,
            request.PoolValueNow,
            request.Cash,
            request.DestinationAccountId,
            request.Notes,
            request.IsFullWindDown);

        Result<RecordRedemptionResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/pools/{id}", response));
    }

    private static async Task<IResult> CloseDistribution(
        Guid id,
        DistributionRequest request,
        ICommandHandler<CloseDistributionCommand, CloseDistributionResponse> handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DistributionPayout>? payouts = request.Payouts is null
            ? null
            : [.. request.Payouts.Select(p => new DistributionPayout(p.ParticipantId, p.Cash))];

        var command = new CloseDistributionCommand(id, request.PoolValueNow, payouts, request.Notes);

        Result<CloseDistributionResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/pools/{id}", response));
    }

    private static async Task<IResult> SettleDistribution(
        Guid id,
        Guid eventId,
        SettleDistributionRequest request,
        ICommandHandler<SettleDistributionCommand, SettleDistributionResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new SettleDistributionCommand(id, eventId, request.SettledOn, request.Notes);

        Result<SettleDistributionResponse> result = await handler.Handle(command, cancellationToken);

        // Settling mutates an existing event rather than creating a resource, so
        // this is a 200 with the payment details, not a 201.
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> RecordCostReimbursement(
        Guid id,
        CostReimbursementRequest request,
        ICommandHandler<RecordCostReimbursementCommand, RecordCostReimbursementResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new RecordCostReimbursementCommand(
            id,
            request.Amount,
            request.PoolValueNow,
            request.Notes);

        Result<RecordCostReimbursementResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> DeleteEvent(
        Guid id,
        Guid eventId,
        ICommandHandler<DeletePoolEventCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new DeletePoolEventCommand(id, eventId), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> ArchivePool(
        Guid id,
        ICommandHandler<ArchivePoolCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new ArchivePoolCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }
}
