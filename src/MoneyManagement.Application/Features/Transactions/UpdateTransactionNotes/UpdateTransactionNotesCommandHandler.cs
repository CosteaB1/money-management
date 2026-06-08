using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.UpdateTransactionNotes;

internal sealed class UpdateTransactionNotesCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateTransactionNotesCommand>
{
    public async Task<Result> Handle(UpdateTransactionNotesCommand command, CancellationToken cancellationToken)
    {
        // The IsDeleted query filter on Transactions keeps soft-deleted rows out.
        Transaction? transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);

        if (transaction is null)
        {
            return Result.Failure(TransactionErrors.NotFound(command.Id));
        }

        // Notes carry no budget/report/balance meaning, so unlike category there
        // is no FX conversion or domain-event side-effect to coordinate.
        transaction.SetNotes(command.Notes);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
