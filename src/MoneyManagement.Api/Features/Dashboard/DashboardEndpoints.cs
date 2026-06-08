using System.Globalization;
using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;
using MoneyManagement.Application.Features.Dashboard.GetSummary;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Dashboard;

public sealed class DashboardEndpoints : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/dashboard").WithTags("Dashboard");

        group.MapGet("/summary", GetSummary);
        group.MapGet("/net-worth-trend", GetNetWorthTrend);
    }

    private static async Task<IResult> GetSummary(
        IQueryHandler<GetSummaryQuery, DashboardSummaryDto> handler,
        CancellationToken cancellationToken,
        string? month = null)
    {
        DateOnly? parsed = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateOnly.TryParseExact(
                    $"{month}-01",
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly value))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    type: "dashboard.invalid_month",
                    detail: "month must be in YYYY-MM format.");
            }

            parsed = value;
        }

        Result<DashboardSummaryDto> result = await handler.Handle(
            new GetSummaryQuery(parsed),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetNetWorthTrend(
        IQueryHandler<GetNetWorthTrendQuery, IReadOnlyList<NetWorthTrendPointDto>> handler,
        CancellationToken cancellationToken,
        int months = 6)
    {
        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await handler.Handle(
            new GetNetWorthTrendQuery(months),
            cancellationToken);

        return result.Match(Results.Ok);
    }
}
