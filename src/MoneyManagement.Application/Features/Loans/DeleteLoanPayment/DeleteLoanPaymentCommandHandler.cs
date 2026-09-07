using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.DeleteLoanPayment;

internal sealed class DeleteLoanPaymentCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter) : ICommandHandler<DeleteLoanPaymentCommand>
{
    public async Task<Result> Handle(DeleteLoanPaymentCommand command, CancellationToken cancellationToken)
    {
        // The payment must belong to the loan named in the route — a valid
        // payment id under the wrong loan is treated as not-found rather than
        // leaking cross-loan access.
        LoanPayment? payment = await db.LoanPayments
            .FirstOrDefaultAsync(
                p => p.Id == command.PaymentId && p.LoanId == command.LoanId,
                cancellationToken);

        if (payment is null)
        {
            return Result.Failure(LoanErrors.PaymentNotFound(command.PaymentId));
        }

        if (payment.TransactionId is Guid transactionId)
        {
            // Normal filtered set: an already soft-deleted transaction is
            // invisible here, which is exactly the skip path — nothing left to
            // roll back on the account side.
            Transaction? transaction = await db.Transactions
                .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

            if (transaction is not null)
            {
                // This handler soft-deletes the row INLINE, so
                // DeleteTransaction's pooled guard never runs on it - the same
                // test has to be applied here or the loans slice becomes the
                // back door to it. Removing a row dated at or before a priced
                // unit event restates the balance every NAV since was struck
                // from, re-pricing units already issued to a third party. The
                // whole command fails: dropping the payment while leaving its
                // transaction (or vice versa) would desynchronise the loan from
                // the account.
                if (await PooledAccountGuard.DeleteRepricesUnitsAsync(
                        db,
                        transaction.AccountId,
                        transaction.Id,
                        transaction.TransactionDate,
                        cancellationToken))
                {
                    return Result.Failure(PoolErrors.DeleteRepricesUnits);
                }

                // FX-convert at the row's own date so downstream event
                // consumers see the same MDL value the create path booked —
                // mirrors DeleteTransactionCommandHandler.
                decimal? amountMdl = await fxConverter.ConvertAsync(
                    transaction.Amount.Amount,
                    transaction.Amount.Currency,
                    ReportingCurrencies.Mdl,
                    transaction.TransactionDate,
                    cancellationToken);

                transaction.MarkDeleted(amountMdl);
            }
        }

        // Hard-remove the payment row. The TransactionDeleted event raised
        // above will re-run RemoveLoanPaymentOnTransactionDeletedHandler after
        // save; it finds no matching payment and no-ops (idempotent by design).
        db.LoanPayments.Remove(payment);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
