using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans;

/// <summary>
/// Drill-down projection for a single <see cref="Loan"/>.
/// Carries the same surface as <see cref="LoanDto"/> plus the creation
/// timestamp, the disbursement-transaction link (resolved to its account when
/// possible), and the full payment history newest-first.
/// </summary>
public sealed record LoanDetailDto(
    Guid Id,
    LoanDirection Direction,
    string Counterparty,
    decimal Principal,
    string Currency,
    DateOnly LoanDate,
    decimal TotalRepaid,
    decimal Outstanding,
    decimal? OutstandingMdl,
    bool MissingFxRate,
    LoanStatus Status,
    int PaymentCount,
    string? Notes,
    bool IsArchived,
    DateTime CreatedOn,
    Guid? DisbursementTransactionId,
    Guid? DisbursementAccountId,
    string? DisbursementAccountName,
    IReadOnlyList<LoanPaymentDto> Payments);

/// <summary>
/// One row in a loan's payment history. <see cref="AccountId"/> /
/// <see cref="AccountName"/> resolve through the linked transaction and are
/// <c>null</c> when the payment was recorded without an account (or the
/// transaction no longer resolves).
/// </summary>
public sealed record LoanPaymentDto(
    Guid Id,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn,
    Guid? TransactionId,
    Guid? AccountId,
    string? AccountName,
    string? Notes);
