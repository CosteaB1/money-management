using Microsoft.AspNetCore.Mvc;
using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Application.Features.Imports.CommitImport;
using MoneyManagement.Application.Features.Imports.ParseStatement;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Imports;

public sealed class ImportEndpoints : IEndpoint
{
    private const long MaxFileSize = 5 * 1024 * 1024;

    public sealed record CommitImportRequest(
        Guid AccountId,
        string FileName,
        string FileHash,
        BankSource BankSource,
        IReadOnlyList<CommitTransactionRequest> Transactions,
        IReadOnlyList<LearnedCategoryPatternRequest>? LearnedPatterns = null);

    public sealed record CommitTransactionRequest(
        DateOnly TransactionDate,
        TransactionDirection Direction,
        decimal Amount,
        string Description,
        Guid? CategoryId,
        decimal? OriginalAmount,
        string? OriginalCurrency,
        bool IsTransfer = false,
        Guid? CounterAccountId = null,
        decimal? CounterAmount = null,
        string? Notes = null);

    public sealed record LearnedCategoryPatternRequest(
        string Keyword,
        Guid CategoryId);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/imports").WithTags("Imports");

        group.MapPost("/parse", ParseStatement)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxFileSize));

        group.MapPost("/commit", CommitImport);
    }

    private static async Task<IResult> ParseStatement(
        HttpRequest request,
        ICommandHandler<ParseStatementCommand, StatementPreviewDto> handler,
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
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "File exceeds 5MB limit.");
        }

        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only PDF files are accepted.");
        }

        if (!Guid.TryParse(form["accountId"], out Guid accountId) || accountId == Guid.Empty)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "accountId is required.");
        }

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        var command = new ParseStatementCommand(memoryStream.ToArray(), file.FileName, accountId);
        Result<StatementPreviewDto> result = await handler.Handle(command, cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> CommitImport(
        CommitImportRequest request,
        ICommandHandler<CommitImportCommand, CommitResultDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new CommitImportCommand(
            request.AccountId,
            request.FileName,
            request.FileHash,
            request.BankSource,
            request.Transactions
                .Select(t => new TransactionToImport(
                    t.TransactionDate,
                    t.Direction,
                    t.Amount,
                    t.Description,
                    t.CategoryId,
                    t.OriginalAmount,
                    t.OriginalCurrency,
                    t.IsTransfer,
                    t.CounterAccountId,
                    t.CounterAmount,
                    Notes: t.Notes))
                .ToList(),
            request.LearnedPatterns?
                .Select(p => new LearnedCategoryPattern(p.Keyword, p.CategoryId))
                .ToList() ?? []);

        Result<CommitResultDto> result = await handler.Handle(command, cancellationToken);
        return result.Match(dto => Results.Created($"/imports/{dto.ImportBatchId}", dto));
    }
}
