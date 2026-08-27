using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.EventHandlers;

/// <summary>
/// Keeps the loan side consistent when a loan-linked <see cref="Transaction"/>
/// is soft-deleted from the transactions page: matching payment rows are
/// removed (hard delete) and any loan pointing at the transaction as its
/// disbursement is de-linked.
/// </summary>
/// <remarks>
/// <para>Fast-gate: loan-synthesized rows are always transfer-flagged, so
/// non-transfer deletions return without touching the database.</para>
/// <para>Idempotent by design — <c>DeleteLoanPaymentCommandHandler</c> also
/// soft-deletes the linked transaction, so this handler fires afterward, finds
/// no matching payment or loan, and no-ops without saving.</para>
/// </remarks>
internal sealed class RemoveLoanPaymentOnTransactionDeletedHandler(
    IApplicationDbContext db,
    ILogger<RemoveLoanPaymentOnTransactionDeletedHandler> logger)
    : IDomainEventHandler<TransactionDeletedDomainEvent>
{
    public async Task Handle(TransactionDeletedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        if (!domainEvent.IsTransfer)
        {
            return;
        }

        bool changed = false;

        List<LoanPayment> payments = await db.LoanPayments
            .Where(p => p.TransactionId == domainEvent.TransactionId)
            .ToListAsync(cancellationToken);

        if (payments.Count > 0)
        {
            db.LoanPayments.RemoveRange(payments);
            changed = true;

            logger.LogInformation(
                "Removed {Count} loan payment(s) linked to soft-deleted transaction {TransactionId}",
                payments.Count,
                domainEvent.TransactionId);
        }

        // IgnoreQueryFilters: an archived loan's disbursement link must be
        // cleared too, or it would dangle forever behind the archive filter.
        List<Loan> loans = await db.Loans
            .IgnoreQueryFilters()
            .Where(l => l.DisbursementTransactionId == domainEvent.TransactionId)
            .ToListAsync(cancellationToken);

        foreach (Loan loan in loans)
        {
            loan.ClearDisbursementTransaction();
            changed = true;

            logger.LogInformation(
                "Cleared disbursement link on loan {LoanId} for soft-deleted transaction {TransactionId}",
                loan.Id,
                domainEvent.TransactionId);
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
