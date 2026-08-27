using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans.CreateLoan;

/// <summary>
/// Creates a loan. When <paramref name="AccountId"/> is set, the disbursement
/// is also written as a transfer-flagged transaction on that account (money
/// actually moved), atomically with the loan row.
/// </summary>
public sealed record CreateLoanCommand(
    LoanDirection Direction,
    string Counterparty,
    decimal Principal,
    string Currency,
    DateOnly LoanDate,
    Guid? AccountId,
    string? Notes) : ICommand<CreateLoanResponse>;

public sealed record CreateLoanResponse(Guid Id);
