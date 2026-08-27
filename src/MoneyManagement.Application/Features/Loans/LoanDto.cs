using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans;

/// <summary>
/// Read-side projection over a <see cref="Loan"/>. All native
/// monetary fields (<see cref="Principal"/>, <see cref="TotalRepaid"/>,
/// <see cref="Outstanding"/>) are in the loan's own <see cref="Currency"/>;
/// <see cref="OutstandingMdl"/> is the reporting-currency conversion at
/// today's rate — <c>null</c> (with <see cref="MissingFxRate"/> flipped) when
/// no usable rate exists, same contract as <c>AccountDto.BalanceMdl</c>.
/// </summary>
public sealed record LoanDto(
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
    bool IsArchived);
