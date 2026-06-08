using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Accounts.ArchiveAccount;
using MoneyManagement.Application.Features.Accounts.CreateAccount;
using MoneyManagement.Application.Features.Accounts.DeleteAccount;
using MoneyManagement.Application.Features.Accounts.GetAccountDetail;
using MoneyManagement.Application.Features.Accounts.GetAccounts;
using MoneyManagement.Application.Features.Accounts.UnarchiveAccount;
using MoneyManagement.Application.Features.Accounts.UpdateAccount;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Accounts;

public sealed class AccountEndpoints : IEndpoint
{
    public sealed record CreateAccountRequest(
        string Name,
        AccountType Type,
        decimal Balance,
        string Currency,
        DateOnly OpeningDate,
        string? Notes);

    public sealed record UpdateAccountRequest(
        string Name,
        string? Notes);

    public sealed record BalanceChangeRequest(
        BalanceChangeKind Kind,
        decimal Value,
        DateOnly Date,
        string? Notes);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/accounts").WithTags("Accounts");

        group.MapPost("/", CreateAccount);
        group.MapGet("/", GetAccounts);
        group.MapGet("/{id:guid}", GetAccountDetail);
        group.MapPut("/{id:guid}", UpdateAccount);
        group.MapDelete("/{id:guid}", ArchiveAccount);
        group.MapDelete("/{id:guid}/permanent", DeleteAccount);
        group.MapPost("/{id:guid}/unarchive", UnarchiveAccount);
        group.MapPost("/{id:guid}/balance-changes", AdjustBalance);
    }

    private static async Task<IResult> CreateAccount(
        CreateAccountRequest request,
        ICommandHandler<CreateAccountCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateAccountCommand(
            request.Name,
            request.Type,
            request.Balance,
            request.Currency,
            request.OpeningDate,
            request.Notes);

        Result<Guid> result = await handler.Handle(command, cancellationToken);
        return result.Match(id => Results.Created($"/accounts/{id}", new { id }));
    }

    private static async Task<IResult> GetAccounts(
        IQueryHandler<GetAccountsQuery, IReadOnlyList<AccountDto>> handler,
        CancellationToken cancellationToken,
        bool includeArchived = false)
    {
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(new GetAccountsQuery(includeArchived), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetAccountDetail(
        Guid id,
        IQueryHandler<GetAccountDetailQuery, AccountDetailDto> handler,
        CancellationToken cancellationToken)
    {
        Result<AccountDetailDto> result = await handler.Handle(new GetAccountDetailQuery(id), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> UpdateAccount(
        Guid id,
        UpdateAccountRequest request,
        ICommandHandler<UpdateAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateAccountCommand(id, request.Name, request.Notes);
        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> ArchiveAccount(
        Guid id,
        ICommandHandler<ArchiveAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new ArchiveAccountCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> UnarchiveAccount(
        Guid id,
        ICommandHandler<UnarchiveAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new UnarchiveAccountCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> DeleteAccount(
        Guid id,
        ICommandHandler<DeleteAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new DeleteAccountCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> AdjustBalance(
        Guid id,
        BalanceChangeRequest request,
        ICommandHandler<AdjustBalanceCommand, AdjustBalanceResult> handler,
        CancellationToken cancellationToken)
    {
        var command = new AdjustBalanceCommand(id, request.Kind, request.Value, request.Date, request.Notes);
        Result<AdjustBalanceResult> result = await handler.Handle(command, cancellationToken);

        return result.Match(adjustment => Results.Created(
            $"/transactions/{adjustment.TransactionId}",
            new { transactionId = adjustment.TransactionId, delta = adjustment.Delta }));
    }
}
