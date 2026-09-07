using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.DeleteAccount;

/// <summary>
/// Permanently removes an account row. Unlike archiving, this is a hard delete
/// and only succeeds when the account has no linked records. Any linked
/// transaction (as the primary or counter account), import batch, savings goal
/// or capital pool blocks the delete with a 409 Conflict so the user archives
/// instead.
/// </summary>
internal sealed class DeleteAccountCommandHandler(IApplicationDbContext db)
    : ICommandHandler<DeleteAccountCommand>
{
    public async Task<Result> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        // Archived accounts must be deletable, so bypass the IsArchived filter.
        Account? account = await db.Accounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure(AccountErrors.NotFound(command.Id));
        }

        // Guard: any linked record blocks the hard delete. IgnoreQueryFilters so
        // soft-deleted transactions and archived goals still count — they remain
        // FK-bound rows in the database. The SavingsGoal -> Account FK is
        // ON DELETE RESTRICT, so a linked goal would throw at the DB; we
        // pre-check and return a friendly Conflict instead.
        bool hasTransactions = await db.Transactions
            .IgnoreQueryFilters()
            .AnyAsync(t => t.AccountId == command.Id || t.CounterAccountId == command.Id, cancellationToken);

        bool hasImports = await db.ImportBatches
            .AnyAsync(b => b.AccountId == command.Id, cancellationToken);

        bool hasGoals = await db.SavingsGoals
            .IgnoreQueryFilters()
            .AnyAsync(g => g.LinkedAccountId == command.Id, cancellationToken);

        // pools.account_id is ON DELETE RESTRICT too, and a pool can exist with
        // NO transactions at all: a zero-delta inception mark writes no row and
        // the seed carries no cash leg by design. Without this pre-check such an
        // account sails past the three checks above and dies on
        // fk_pools_accounts_account_id as an unhandled 500 with no errorCode,
        // where the user needed "this account holds a pool, archive it instead".
        // IgnoreQueryFilters: an archived pool still holds the FK.
        bool hasPool = await db.Pools
            .IgnoreQueryFilters()
            .AnyAsync(p => p.AccountId == command.Id, cancellationToken);

        if (hasTransactions || hasImports || hasGoals || hasPool)
        {
            return Result.Failure(AccountErrors.HasLinkedRecords(command.Id));
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
