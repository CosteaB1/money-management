using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.CreateLoan;

internal sealed class CreateLoanCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<CreateLoanCommand, CreateLoanResponse>
{
    public async Task<Result<CreateLoanResponse>> Handle(
        CreateLoanCommand command,
        CancellationToken cancellationToken)
    {
        var principal = new Money(command.Principal, command.Currency);
        Result<Loan> loanResult = Loan.Create(
            command.Direction,
            command.Counterparty,
            principal,
            command.LoanDate,
            command.Notes,
            clock);

        if (loanResult.IsFailure)
        {
            return Result.Failure<CreateLoanResponse>(loanResult.Error);
        }

        Loan loan = loanResult.Value;

        if (command.AccountId is Guid accountId)
        {
            // Archived accounts can't receive new movements; the explicit
            // predicate collapses archived and missing ids into the same
            // NotFound — defense-in-depth alongside any query filter, and the
            // same policy the rest of the app applies to archived accounts.
            Account? account = await db.Accounts
                .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsArchived, cancellationToken);

            if (account is null)
            {
                return Result.Failure<CreateLoanResponse>(AccountErrors.NotFound(accountId));
            }

            // A disbursement leg is cash landing on the account with NO unit
            // event, so net worth shares it pro-rata with the outside investors
            // - and it lands twice, because LoanExternalClaimSource books the
            // matching claim on top. The leg is IsTransfer-flagged and written
            // inline here, so neither CreateTransaction's nor CreateTransfer's
            // guard ever sees it; this is the same rule, at its own door.
            if (await PooledAccountGuard.IsPooledAsync(db, accountId, cancellationToken))
            {
                return Result.Failure<CreateLoanResponse>(PoolErrors.LoanMovementBlocked);
            }

            if (!string.Equals(account.Balance.Currency, loan.Principal.Currency, StringComparison.Ordinal))
            {
                return Result.Failure<CreateLoanResponse>(LoanErrors.AccountCurrencyMismatch);
            }

            (TransactionDirection txDirection, string description) =
                LoanMovements.ForDisbursement(loan.Direction, loan.Counterparty);

            var money = new Money(command.Principal, account.Balance.Currency);

            // MDL-equivalent at the movement's own date — same contract as
            // CreateTransferCommandHandler. Nullable propagates; downstream
            // consumers must tolerate a missing rate.
            decimal? amountMdl = await fxConverter.ConvertAsync(
                money.Amount,
                account.Balance.Currency,
                ReportingCurrencies.Mdl,
                command.LoanDate,
                cancellationToken);

            Result<Transaction> txResult = Transaction.Create(
                accountId,
                command.LoanDate,
                txDirection,
                money,
                description,
                TransactionSource.Manual,
                categoryId: SeededCategories.LoanId,
                importBatchId: null,
                originalAmount: null,
                originalCurrency: null,
                // Transfer-flagged so the movement stays out of P&L aggregates
                // (Investment/Withdrawal precedent); no counter account — the
                // other side of a personal loan is outside the app.
                isTransfer: true,
                counterAccountId: null,
                isAdjustment: false,
                amountMdl: amountMdl,
                notes: loan.Notes);

            if (txResult.IsFailure)
            {
                return Result.Failure<CreateLoanResponse>(txResult.Error);
            }

            Transaction transaction = txResult.Value;
            db.Transactions.Add(transaction);
            loan.SetDisbursementTransaction(transaction.Id);
        }

        db.Loans.Add(loan);

        // Single SaveChangesAsync so the loan and its disbursement transaction
        // land (or fail) atomically.
        await db.SaveChangesAsync(cancellationToken);

        return new CreateLoanResponse(loan.Id);
    }
}
