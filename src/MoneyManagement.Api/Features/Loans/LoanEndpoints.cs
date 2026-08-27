using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Loans;
using MoneyManagement.Application.Features.Loans.ArchiveLoan;
using MoneyManagement.Application.Features.Loans.CreateLoan;
using MoneyManagement.Application.Features.Loans.DeleteLoanPayment;
using MoneyManagement.Application.Features.Loans.GetLoanDetail;
using MoneyManagement.Application.Features.Loans.GetLoans;
using MoneyManagement.Application.Features.Loans.RecordLoanPayment;
using MoneyManagement.Application.Features.Loans.UnarchiveLoan;
using MoneyManagement.Application.Features.Loans.UpdateLoan;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Loans;

public sealed class LoanEndpoints : IEndpoint
{
    public sealed record CreateLoanRequest(
        LoanDirection Direction,
        string Counterparty,
        decimal Principal,
        string Currency,
        DateOnly LoanDate,
        Guid? AccountId,
        string? Notes);

    public sealed record UpdateLoanRequest(string Counterparty, string? Notes);

    public sealed record RecordLoanPaymentRequest(
        decimal Amount,
        DateOnly OccurredOn,
        Guid? AccountId,
        string? Notes);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/loans").WithTags("Loans");

        group.MapPost("/", CreateLoan);
        group.MapGet("/", GetLoans);
        group.MapGet("/{id:guid}", GetLoanDetail);
        group.MapPut("/{id:guid}", UpdateLoan);
        group.MapPost("/{id:guid}/payments", RecordPayment);
        group.MapDelete("/{id:guid}/payments/{paymentId:guid}", DeletePayment);
        group.MapDelete("/{id:guid}", ArchiveLoan);
        group.MapPost("/{id:guid}/unarchive", UnarchiveLoan);
    }

    private static async Task<IResult> CreateLoan(
        CreateLoanRequest request,
        ICommandHandler<CreateLoanCommand, CreateLoanResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateLoanCommand(
            request.Direction,
            request.Counterparty,
            request.Principal,
            request.Currency,
            request.LoanDate,
            request.AccountId,
            request.Notes);

        Result<CreateLoanResponse> result = await handler.Handle(command, cancellationToken);

        return result.Match(response => Results.Created($"/loans/{response.Id}", new { id = response.Id }));
    }

    private static async Task<IResult> GetLoans(
        IQueryHandler<GetLoansQuery, IReadOnlyList<LoanDto>> handler,
        CancellationToken cancellationToken,
        bool includeArchived = false)
    {
        Result<IReadOnlyList<LoanDto>> result = await handler.Handle(new GetLoansQuery(includeArchived), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> GetLoanDetail(
        Guid id,
        IQueryHandler<GetLoanDetailQuery, LoanDetailDto> handler,
        CancellationToken cancellationToken)
    {
        Result<LoanDetailDto> result = await handler.Handle(new GetLoanDetailQuery(id), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> UpdateLoan(
        Guid id,
        UpdateLoanRequest request,
        ICommandHandler<UpdateLoanCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateLoanCommand(id, request.Counterparty, request.Notes);

        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> RecordPayment(
        Guid id,
        RecordLoanPaymentRequest request,
        ICommandHandler<RecordLoanPaymentCommand, RecordLoanPaymentResponse> handler,
        CancellationToken cancellationToken)
    {
        var command = new RecordLoanPaymentCommand(
            id,
            request.Amount,
            request.OccurredOn,
            request.AccountId,
            request.Notes);

        Result<RecordLoanPaymentResponse> result = await handler.Handle(command, cancellationToken);

        // Payments have no standalone GET; the loan detail page is where they
        // surface, so Location points at the parent resource.
        return result.Match(response => Results.Created($"/loans/{id}", new { id = response.Id }));
    }

    private static async Task<IResult> DeletePayment(
        Guid id,
        Guid paymentId,
        ICommandHandler<DeleteLoanPaymentCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new DeleteLoanPaymentCommand(id, paymentId), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> ArchiveLoan(
        Guid id,
        ICommandHandler<ArchiveLoanCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new ArchiveLoanCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> UnarchiveLoan(
        Guid id,
        ICommandHandler<UnarchiveLoanCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new UnarchiveLoanCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }
}
