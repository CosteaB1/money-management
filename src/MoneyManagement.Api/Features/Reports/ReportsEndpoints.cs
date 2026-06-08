using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Reports.ExportTransactionsCsv;
using MoneyManagement.Application.Features.Reports.GetBalanceOverTime;
using MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;
using MoneyManagement.Application.Features.Reports.GetMonthlySummary;
using MoneyManagement.Application.Features.Reports.GetTopPayees;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Reports;

public sealed class ReportsEndpoints : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/monthly-summary", GetMonthlySummary);
        group.MapGet("/category-breakdown", GetCategoryBreakdown);
        group.MapGet("/balance-over-time", GetBalanceOverTime);
        group.MapGet("/top-payees", GetTopPayees);
        group.MapGet("/transactions.csv", ExportTransactionsCsv);
    }

    private static async Task<IResult> GetMonthlySummary(
        IQueryHandler<GetMonthlySummaryQuery, IReadOnlyList<MonthlySummaryPointDto>> handler,
        CancellationToken cancellationToken,
        string? from = null,
        string? to = null)
    {
        if (!TryParseOptionalMonth(from, out DateOnly? parsedFrom))
        {
            return InvalidMonth(nameof(from));
        }

        if (!TryParseOptionalMonth(to, out DateOnly? parsedTo))
        {
            return InvalidMonth(nameof(to));
        }

        Result<IReadOnlyList<MonthlySummaryPointDto>> result = await handler.Handle(
            new GetMonthlySummaryQuery(parsedFrom, parsedTo),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetCategoryBreakdown(
        IQueryHandler<GetCategoryBreakdownQuery, CategoryBreakdownDto> handler,
        CancellationToken cancellationToken,
        DateOnly from,
        DateOnly to,
        TransactionDirection direction)
    {
        Result<CategoryBreakdownDto> result = await handler.Handle(
            new GetCategoryBreakdownQuery(from, to, direction),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetBalanceOverTime(
        IQueryHandler<GetBalanceOverTimeQuery, IReadOnlyList<BalancePointDto>> handler,
        CancellationToken cancellationToken,
        Guid accountId,
        DateOnly from,
        DateOnly to,
        BalanceInterval interval = BalanceInterval.Monthly)
    {
        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(accountId, from, to, interval),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetTopPayees(
        IQueryHandler<GetTopPayeesQuery, IReadOnlyList<TopPayeeDto>> handler,
        CancellationToken cancellationToken,
        DateOnly from,
        DateOnly to,
        TransactionDirection direction,
        int limit = 10)
    {
        Result<IReadOnlyList<TopPayeeDto>> result = await handler.Handle(
            new GetTopPayeesQuery(from, to, direction, limit),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> ExportTransactionsCsv(
        HttpContext httpContext,
        IQueryHandler<ExportTransactionsCsvQuery, IReadOnlyList<TransactionExportRow>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? categoryId = null,
        TransactionDirection? direction = null,
        bool? isTransfer = null,
        bool? isAdjustment = null)
    {
        Result<IReadOnlyList<TransactionExportRow>> result = await handler.Handle(
            new ExportTransactionsCsvQuery(accountId, from, to, categoryId, direction, isTransfer, isAdjustment),
            cancellationToken);

        if (result.IsFailure)
        {
            return ((Result)result).ToProblemDetails();
        }

        string fileName = $"transactions_{DateTime.UtcNow:yyyy-MM-dd}.csv";

        HttpResponse response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        await using var writer = new StreamWriter(response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Header — keep snake_case so consumers (Excel, Pandas, sqlite import)
        // can use the row as-is without renaming.
        await writer.WriteLineAsync(CsvWriter.JoinRow(new[]
        {
            "transaction_date",
            "account",
            "category",
            "direction",
            "amount",
            "currency",
            "amount_mdl",
            "description",
            "is_transfer",
            "is_adjustment",
        }));

        foreach (TransactionExportRow row in result.Value)
        {
            string[] fields =
            [
                CsvWriter.FormatDate(row.TransactionDate),
                CsvWriter.EscapeField(row.AccountName),
                CsvWriter.EscapeField(row.CategoryName),
                row.Direction.ToString(),
                CsvWriter.FormatDecimal(row.Amount),
                row.Currency,
                row.AmountMdl is { } mdl ? CsvWriter.FormatDecimal(mdl) : string.Empty,
                CsvWriter.EscapeField(row.Description),
                row.IsTransfer ? "true" : "false",
                row.IsAdjustment ? "true" : "false",
            ];

            await writer.WriteLineAsync(CsvWriter.JoinRow(fields));
        }

        await writer.FlushAsync(cancellationToken);
        return Results.Empty;
    }

    private static bool TryParseOptionalMonth(string? value, out DateOnly? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                $"{value}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static IResult InvalidMonth(string paramName) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            type: "reports.invalid_month",
            detail: $"{paramName} must be in YYYY-MM format.");
}
