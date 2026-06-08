using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Common;
using MoneyManagement.Application.Features.Transactions;
using MoneyManagement.Application.Features.Transactions.CreateTransaction;
using MoneyManagement.Application.Features.Transactions.DeleteTransaction;
using MoneyManagement.Application.Features.Transactions.GetTransactions;
using MoneyManagement.Application.Features.Transactions.UpdateTransactionCategory;
using MoneyManagement.Application.Features.Transactions.UpdateTransactionNotes;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Transactions;

public sealed class TransactionEndpoints : IEndpoint
{
    public sealed record CreateTransactionRequest(
        Guid AccountId,
        DateOnly TransactionDate,
        TransactionDirection Direction,
        decimal Amount,
        string Description,
        Guid? CategoryId,
        decimal? OriginalAmount,
        string? OriginalCurrency,
        bool IsTransfer = false,
        Guid? CounterAccountId = null,
        bool IsAdjustment = false,
        string? Currency = null,
        string? Notes = null);

    public sealed record UpdateTransactionCategoryRequest(Guid? CategoryId);

    public sealed record UpdateTransactionNotesRequest(string? Notes);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/transactions").WithTags("Transactions");

        group.MapPost("/", CreateTransaction);
        group.MapGet("/", GetTransactions);
        group.MapPut("/{id:guid}/category", UpdateTransactionCategory);
        group.MapPut("/{id:guid}/notes", UpdateTransactionNotes);
        group.MapDelete("/{id:guid}", DeleteTransaction);
    }

    private static async Task<IResult> CreateTransaction(
        CreateTransactionRequest request,
        ICommandHandler<CreateTransactionCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateTransactionCommand(
            request.AccountId,
            request.TransactionDate,
            request.Direction,
            request.Amount,
            request.Description,
            request.CategoryId,
            request.OriginalAmount,
            request.OriginalCurrency,
            request.IsTransfer,
            request.CounterAccountId,
            request.IsAdjustment,
            request.Currency,
            request.Notes);

        Result<Guid> result = await handler.Handle(command, cancellationToken);
        return result.Match(id => Results.Created($"/transactions/{id}", new { id }));
    }

    private static async Task<IResult> GetTransactions(
        IQueryHandler<GetTransactionsQuery, PagedResult<TransactionDto>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? categoryId = null,
        TransactionDirection? direction = null,
        bool? isTransfer = null,
        bool? isAdjustment = null,
        int page = 1,
        int pageSize = 25)
    {
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(accountId, from, to, categoryId, direction, isTransfer, isAdjustment, page, pageSize),
            cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> UpdateTransactionCategory(
        Guid id,
        UpdateTransactionCategoryRequest request,
        ICommandHandler<UpdateTransactionCategoryCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTransactionCategoryCommand(id, request.CategoryId);

        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> UpdateTransactionNotes(
        Guid id,
        UpdateTransactionNotesRequest request,
        ICommandHandler<UpdateTransactionNotesCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTransactionNotesCommand(id, request.Notes);

        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> DeleteTransaction(
        Guid id,
        ICommandHandler<DeleteTransactionCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new DeleteTransactionCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }
}
