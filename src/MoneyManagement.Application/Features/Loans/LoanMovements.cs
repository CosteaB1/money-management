using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Loans;

/// <summary>
/// Maps a loan's direction to the shape of the transaction synthesized for a
/// money movement. Shared by the create-loan (disbursement) and record-payment
/// handlers so the direction/description matrix lives in one place:
/// <list type="bullet">
///   <item><description>Received disbursement → Income, "Loan from {counterparty}"</description></item>
///   <item><description>Received payment → Expense, "Loan repayment to {counterparty}"</description></item>
///   <item><description>Given disbursement → Expense, "Loan to {counterparty}"</description></item>
///   <item><description>Given payment → Income, "Loan repayment from {counterparty}"</description></item>
/// </list>
/// The synthesized rows are always <c>IsTransfer = true</c> so every existing
/// income/expense aggregate (dashboard, reports, budget event handlers)
/// filters them out with zero changes — the Phase-4 Investment/Withdrawal
/// balance-change precedent.
/// </summary>
internal static class LoanMovements
{
    public static (TransactionDirection Direction, string Description) ForDisbursement(
        LoanDirection loanDirection,
        string counterparty) =>
        loanDirection == LoanDirection.Received
            ? (TransactionDirection.Income, $"Loan from {counterparty}")
            : (TransactionDirection.Expense, $"Loan to {counterparty}");

    public static (TransactionDirection Direction, string Description) ForPayment(
        LoanDirection loanDirection,
        string counterparty) =>
        loanDirection == LoanDirection.Received
            ? (TransactionDirection.Expense, $"Loan repayment to {counterparty}")
            : (TransactionDirection.Income, $"Loan repayment from {counterparty}");
}
