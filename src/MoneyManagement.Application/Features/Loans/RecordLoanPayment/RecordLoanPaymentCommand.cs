using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.RecordLoanPayment;

/// <summary>
/// Records a partial repayment against a loan. When
/// <paramref name="AccountId"/> is set, the movement is also written as a
/// transfer-flagged transaction on that account, atomically with the payment
/// row. The amount is always in the loan's currency (v1 is same-currency
/// only) and may not exceed the outstanding balance.
/// </summary>
public sealed record RecordLoanPaymentCommand(
    Guid LoanId,
    decimal Amount,
    DateOnly OccurredOn,
    Guid? AccountId,
    string? Notes) : ICommand<RecordLoanPaymentResponse>;

public sealed record RecordLoanPaymentResponse(Guid Id);
