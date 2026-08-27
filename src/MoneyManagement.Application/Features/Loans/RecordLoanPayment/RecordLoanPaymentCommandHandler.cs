using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.RecordLoanPayment;

internal sealed class RecordLoanPaymentCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : ICommandHandler<RecordLoanPaymentCommand, RecordLoanPaymentResponse>
{
    public async Task<Result<RecordLoanPaymentResponse>> Handle(
        RecordLoanPaymentCommand command,
        CancellationToken cancellationToken)
    {
        // The is_archived = false global query filter hides archived loans, so
        // recording against one 404s naturally; the explicit predicate is
        // defense-in-depth for unit tests that bypass model configuration.
        Loan? loan = await db.Loans
            .FirstOrDefaultAsync(l => l.Id == command.LoanId && !l.IsArchived, cancellationToken);

        if (loan is null)
        {
            return Result.Failure<RecordLoanPaymentResponse>(LoanErrors.NotFound(command.LoanId));
        }

        // Overpayment guard: payments may never push total repaid past the
        // principal. Same-currency-only (enforced by the payment factory), so
        // the comparison needs no FX.
        decimal totalRepaid = await db.LoanPayments
            .Where(p => p.LoanId == loan.Id)
            .SumAsync(p => p.Amount.Amount, cancellationToken);

        decimal outstanding = loan.Principal.Amount - totalRepaid;
        if (command.Amount > outstanding)
        {
            return Result.Failure<RecordLoanPaymentResponse>(LoanErrors.PaymentExceedsOutstanding);
        }

        Result<LoanPayment> paymentResult = LoanPayment.Create(
            loan.Id,
            new Money(command.Amount, loan.Principal.Currency),
            loan.Principal.Currency,
            loan.LoanDate,
            command.OccurredOn,
            command.Notes,
            clock);

        if (paymentResult.IsFailure)
        {
            return Result.Failure<RecordLoanPaymentResponse>(paymentResult.Error);
        }

        LoanPayment payment = paymentResult.Value;

        if (command.AccountId is Guid accountId)
        {
            // Archived accounts can't receive new movements; archived and
            // missing ids collapse into the same NotFound (see CreateLoan).
            Account? account = await db.Accounts
                .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsArchived, cancellationToken);

            if (account is null)
            {
                return Result.Failure<RecordLoanPaymentResponse>(AccountErrors.NotFound(accountId));
            }

            if (!string.Equals(account.Balance.Currency, loan.Principal.Currency, StringComparison.Ordinal))
            {
                return Result.Failure<RecordLoanPaymentResponse>(LoanErrors.AccountCurrencyMismatch);
            }

            (TransactionDirection txDirection, string description) =
                LoanMovements.ForPayment(loan.Direction, loan.Counterparty);

            var money = new Money(command.Amount, account.Balance.Currency);

            decimal? amountMdl = await fxConverter.ConvertAsync(
                money.Amount,
                account.Balance.Currency,
                ReportingCurrencies.Mdl,
                command.OccurredOn,
                cancellationToken);

            Result<Transaction> txResult = Transaction.Create(
                accountId,
                command.OccurredOn,
                txDirection,
                money,
                description,
                TransactionSource.Manual,
                categoryId: SeededCategories.LoanId,
                importBatchId: null,
                originalAmount: null,
                originalCurrency: null,
                isTransfer: true,
                counterAccountId: null,
                isAdjustment: false,
                amountMdl: amountMdl,
                notes: payment.Notes);

            if (txResult.IsFailure)
            {
                return Result.Failure<RecordLoanPaymentResponse>(txResult.Error);
            }

            Transaction transaction = txResult.Value;
            db.Transactions.Add(transaction);
            payment.LinkTransaction(transaction.Id);
        }

        db.LoanPayments.Add(payment);

        // Single SaveChangesAsync so the payment and its transaction land (or
        // fail) atomically.
        await db.SaveChangesAsync(cancellationToken);

        return new RecordLoanPaymentResponse(payment.Id);
    }
}
