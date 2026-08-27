using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.DeleteLoanPayment;

/// <summary>
/// Removes a payment row (hard delete — payments are bookkeeping, not
/// financial events) and soft-deletes its linked transaction, if any, so the
/// account balance rolls back too.
/// </summary>
public sealed record DeleteLoanPaymentCommand(Guid LoanId, Guid PaymentId) : ICommand;
