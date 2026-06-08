using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.CreateTransaction;

internal sealed class CreateTransactionCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : ICommandHandler<CreateTransactionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);

        if (account is null)
        {
            return Result.Failure<Guid>(AccountErrors.NotFound(command.AccountId));
        }

        if (command.CategoryId is { } categoryId)
        {
            bool categoryExists = await db.Categories
                .AnyAsync(c => c.Id == categoryId, cancellationToken);

            if (!categoryExists)
            {
                return Result.Failure<Guid>(CategoryErrors.NotFound(categoryId));
            }
        }

        // Phase 4: transactions inherit their account's currency. The validator
        // accepts any 3-letter ISO code; this cross-entity assertion is the
        // boundary check.
        string accountCurrency = account.Balance.Currency;
        string txCurrency = command.Currency ?? accountCurrency;

        if (!string.Equals(txCurrency, accountCurrency, StringComparison.Ordinal))
        {
            return Result.Failure<Guid>(TransactionErrors.CurrencyMismatchAccount);
        }

        var money = new Money(command.Amount, accountCurrency);

        // Convert to MDL at the transaction's own date so domain-event consumers
        // (e.g. budget spend tracking) get the right reporting-currency figure.
        // Mirrors GetTransactionsQueryHandler's TransactionDto.AmountMdl rule.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            command.Amount,
            accountCurrency,
            ReportingCurrencies.Mdl,
            command.TransactionDate,
            cancellationToken);

        Result<Transaction> transactionResult = Transaction.Create(
            command.AccountId,
            command.TransactionDate,
            command.Direction,
            money,
            command.Description,
            TransactionSource.Manual,
            command.CategoryId,
            importBatchId: null,
            originalAmount: command.OriginalAmount,
            originalCurrency: command.OriginalCurrency,
            isTransfer: command.IsTransfer,
            counterAccountId: command.CounterAccountId,
            isAdjustment: command.IsAdjustment,
            amountMdl: amountMdl,
            notes: command.Notes);

        if (transactionResult.IsFailure)
        {
            return Result.Failure<Guid>(transactionResult.Error);
        }

        Transaction transaction = transactionResult.Value;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        return transaction.Id;
    }
}
