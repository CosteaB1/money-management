using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.DataPortability;
using MoneyManagement.Application.Features.DataPortability.ExportData;
using MoneyManagement.Application.Features.DataPortability.ImportData;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Data;

public sealed class DataEndpoints : IEndpoint
{
    // Backups can be large for a multi-year statement import; 50MB headroom.
    private const long MaxFileSize = 50 * 1024 * 1024;

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/data").WithTags("Data");

        group.MapGet("/export", ExportData);

        group.MapPost("/import", ImportData)
            .DisableAntiforgery();
    }

    private static async Task<IResult> ExportData(
        HttpContext httpContext,
        IQueryHandler<ExportDataQuery, BackupDocument> handler,
        IOptions<JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        Result<BackupDocument> result = await handler.Handle(new ExportDataQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return ((Result)result).ToProblemDetails();
        }

        string fileName = $"moneymanagement-backup-{DateTime.UtcNow:yyyy-MM-dd_HH-mm}.json";

        HttpResponse response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json";
        response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        // Stream straight to the response body using the app's configured JSON
        // options (JsonStringEnumConverter) so enums serialize as names —
        // consistent with every other endpoint and required for round-tripping.
        await JsonSerializer.SerializeAsync(
            response.Body,
            result.Value,
            jsonOptions.Value.SerializerOptions,
            cancellationToken);

        return Results.Empty;
    }

    private static async Task<IResult> ImportData(
        HttpRequest request,
        ICommandHandler<ImportDataCommand, ImportDataResult> handler,
        IOptions<JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Multipart form required.");
        }

        IFormCollection form = await request.ReadFormAsync(cancellationToken);

        IFormFile? file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "File is required.");
        }

        if (file.Length > MaxFileSize)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "File exceeds 50MB limit.");
        }

        BackupDocument? document;
        try
        {
            await using Stream stream = file.OpenReadStream();
            document = await JsonSerializer.DeserializeAsync<BackupDocument>(
                stream,
                jsonOptions.Value.SerializerOptions,
                cancellationToken);
        }
        catch (JsonException ex)
        {
            return ((Result)Result.Failure<ImportDataResult>(
                DataErrors.MalformedBackup($"The uploaded file is not valid backup JSON: {ex.Message}")))
                .ToProblemDetails();
        }

        if (document is null)
        {
            return ((Result)Result.Failure<ImportDataResult>(
                DataErrors.MalformedBackup("The uploaded file deserialized to null.")))
                .ToProblemDetails();
        }

        Result<ImportDataResult> result = await handler.Handle(new ImportDataCommand(document), cancellationToken);
        return result.Match(Results.Ok);
    }
}
