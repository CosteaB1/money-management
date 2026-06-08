using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Common;
using MoneyManagement.Application.Features.FxRates;
using MoneyManagement.Application.Features.FxRates.BackfillBnmRates;
using MoneyManagement.Application.Features.FxRates.ConvertFx;
using MoneyManagement.Application.Features.FxRates.CreateFxRate;
using MoneyManagement.Application.Features.FxRates.DeleteFxRate;
using MoneyManagement.Application.Features.FxRates.GetFxRates;
using MoneyManagement.Application.Features.FxRates.RefreshBnmRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.FxRates;

public sealed class FxRateEndpoints : IEndpoint
{
    public sealed record CreateFxRateRequest(
        string FromCurrency,
        string ToCurrency,
        decimal Rate,
        DateOnly AsOf);

    /// <summary>
    /// Optional body for <c>POST /fx-rates/refresh</c>. Omit or send
    /// <c>{ "date": null }</c> to refresh today's rates; pass an explicit
    /// ISO date to backfill a specific day.
    /// </summary>
    public sealed record RefreshFxRatesRequest(DateOnly? Date);

    /// <summary>
    /// Body for <c>POST /fx-rates/backfill</c>. Replays the single-date refresh
    /// over an inclusive range. Omit <c>to</c> to backfill through today UTC.
    /// </summary>
    public sealed record BackfillFxRatesRequest(DateOnly From, DateOnly? To);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/fx-rates").WithTags("FxRates");

        group.MapPost("/", CreateFxRate);
        group.MapGet("/", GetFxRates);
        group.MapGet("/convert", ConvertFx);
        group.MapDelete("/{id:guid}", DeleteFxRate);
        group.MapPost("/refresh", RefreshFxRates);
        group.MapPost("/backfill", BackfillFxRates);
    }

    private static async Task<IResult> CreateFxRate(
        CreateFxRateRequest request,
        ICommandHandler<CreateFxRateCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateFxRateCommand(
            request.FromCurrency,
            request.ToCurrency,
            request.Rate,
            request.AsOf);

        Result<Guid> result = await handler.Handle(command, cancellationToken);
        return result.Match(id => Results.Created($"/fx-rates/{id}", new { id }));
    }

    private static async Task<IResult> GetFxRates(
        IQueryHandler<GetFxRatesQuery, PagedResult<FxRateDto>> handler,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<FxRateDto>> result = await handler.Handle(new GetFxRatesQuery(page, pageSize), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> ConvertFx(
        IQueryHandler<ConvertFxQuery, ConvertFxResult> handler,
        string from,
        string to,
        DateOnly date,
        decimal amount,
        CancellationToken ct)
    {
        Result<ConvertFxResult> result = await handler.Handle(
            new ConvertFxQuery(from, to, date, amount), ct);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> DeleteFxRate(
        Guid id,
        ICommandHandler<DeleteFxRateCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new DeleteFxRateCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> RefreshFxRates(
        RefreshFxRatesRequest? request,
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler,
        CancellationToken cancellationToken)
    {
        // Synchronous: the caller waits for the BNM fetch + DB upsert. Use a
        // generous client-side timeout in tests — BNM occasionally lags.
        var command = new RefreshBnmRatesCommand(Date: request?.Date, CurrencyFilter: null);
        Result<RefreshBnmRatesResponse> result = await handler.Handle(command, cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> BackfillFxRates(
        BackfillFxRatesRequest request,
        ICommandHandler<BackfillBnmRatesCommand, BackfillBnmRatesResponse> handler,
        CancellationToken ct)
    {
        // Synchronous: walks the range day-by-day, reusing the per-date refresh.
        var command = new BackfillBnmRatesCommand(request.From, request.To);
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, ct);
        return result.Match(Results.Ok);
    }
}
