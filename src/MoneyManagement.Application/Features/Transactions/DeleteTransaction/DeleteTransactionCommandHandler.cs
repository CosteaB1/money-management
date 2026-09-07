using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.DeleteTransaction;

internal sealed class DeleteTransactionCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter) : ICommandHandler<DeleteTransactionCommand>
{
    public async Task<Result> Handle(DeleteTransactionCommand command, CancellationToken cancellationToken)
    {
        Transaction? transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);

        if (transaction is null)
        {
            return Result.Failure(TransactionErrors.NotFound(command.Id));
        }

        // Refuses to delete a row that units have already been priced against.
        // The rule itself lives in PooledAccountGuard because this handler is
        // not the only path that soft-deletes a transaction - DeleteLoanPayment
        // does it inline and has to apply exactly the same test.
        if (await PooledAccountGuard.DeleteRepricesUnitsAsync(
                db,
                transaction.AccountId,
                transaction.Id,
                transaction.TransactionDate,
                cancellationToken))
        {
            return Result.Failure(PoolErrors.DeleteRepricesUnits);
        }

        // FX-convert at the row's own date so the inverse budget update sees
        // the same MDL value the create path booked. Nullable propagates — no
        // usable rate at that date means the budget handler will skip anyway.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            transaction.Amount.Amount,
            transaction.Amount.Currency,
            ReportingCurrencies.Mdl,
            transaction.TransactionDate,
            cancellationToken);

        transaction.MarkDeleted(amountMdl);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
