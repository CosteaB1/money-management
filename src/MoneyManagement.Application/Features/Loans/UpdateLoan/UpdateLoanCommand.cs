using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.UpdateLoan;

/// <summary>
/// Edits the loan's user-mutable metadata — counterparty and notes only.
/// Principal, currency, direction, and loan date are immutable after creation
/// (mirrors <c>UpdateAccountCommand</c>'s name+notes contract).
/// </summary>
public sealed record UpdateLoanCommand(Guid Id, string Counterparty, string? Notes) : ICommand;
