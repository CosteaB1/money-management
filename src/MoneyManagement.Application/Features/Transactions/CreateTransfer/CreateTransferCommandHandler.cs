using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.CreateTransfer;

internal sealed class CreateTransferCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : ICommandHandler<CreateTransferCommand, TransferResult>
{
    public async Task<Result<TransferResult>> Handle(
        CreateTransferCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SourceAccountId == command.DestinationAccountId)
        {
            return Result.Failure<TransferResult>(TransferErrors.SameSourceAndDestination);
        }

        Account? source = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.SourceAccountId, cancellationToken);

        if (source is null)
        {
            return Result.Failure<TransferResult>(TransferErrors.SourceAccountNotFound(command.SourceAccountId));
        }

        Account? destination = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.DestinationAccountId, cancellationToken);

        if (destination is null)
        {
            return Result.Failure<TransferResult>(
                TransferErrors.DestinationAccountNotFound(command.DestinationAccountId));
        }

        bool crossCurrency = !string.Equals(
            source.Balance.Currency,
            destination.Balance.Currency,
            StringComparison.Ordinal);

        // Cross-currency transfers move a different nominal amount into the
        // destination account (e.g. 17,163 MDL out, 1000 USD in). The caller
        // must supply that destination amount; for same-currency transfers the
        // destination amount equals the source amount.
        if (crossCurrency && (command.DestinationAmount is null || command.DestinationAmount.Value <= 0))
        {
            return Result.Failure<TransferResult>(TransferErrors.DestinationAmountRequired);
        }

        decimal destinationAmount = crossCurrency ? command.DestinationAmount!.Value : command.Amount;

        if (command.CategoryId is { } categoryId)
        {
            bool categoryExists = await db.Categories
                .AnyAsync(c => c.Id == categoryId, cancellationToken);

            if (!categoryExists)
            {
                return Result.Failure<TransferResult>(CategoryErrors.NotFound(categoryId));
            }
        }

        // Each leg is denominated in its own account's currency. For
        // same-currency transfers both moneys share the source amount/currency.
        var sourceMoney = new Money(command.Amount, source.Balance.Currency);
        var destinationMoney = new Money(destinationAmount, destination.Balance.Currency);

        // Convert ONCE from the source side - both legs share the same MDL value
        // so the transferred value is conserved in the reporting currency.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            command.Amount,
            source.Balance.Currency,
            ReportingCurrencies.Mdl,
            command.Date,
            cancellationToken);

        Result<Transaction> sourceLegResult = Transaction.Create(
            command.SourceAccountId,
            command.Date,
            TransactionDirection.Expense,
            sourceMoney,
            command.Description,
            TransactionSource.Manual,
            command.CategoryId,
            importBatchId: null,
            // Traceability: when currencies differ, stamp the source leg with the
            // OTHER (destination) leg's amount+currency. Otherwise leave null.
            originalAmount: crossCurrency ? destinationAmount : null,
            originalCurrency: crossCurrency ? destination.Balance.Currency : null,
            isTransfer: true,
            counterAccountId: command.DestinationAccountId,
            amountMdl: amountMdl,
            // Same note on both legs so the annotation shows from either account.
            notes: command.Notes);

        if (sourceLegResult.IsFailure)
        {
            return Result.Failure<TransferResult>(sourceLegResult.Error);
        }

        Result<Transaction> destinationLegResult = Transaction.Create(
            command.DestinationAccountId,
            command.Date,
            TransactionDirection.Income,
            destinationMoney,
            command.Description,
            TransactionSource.Manual,
            command.CategoryId,
            importBatchId: null,
            // Cross-stamp the destination leg with the source leg's amount+currency.
            originalAmount: crossCurrency ? command.Amount : null,
            originalCurrency: crossCurrency ? source.Balance.Currency : null,
            isTransfer: true,
            counterAccountId: command.SourceAccountId,
            amountMdl: amountMdl,
            // Same note on both legs so the annotation shows from either account.
            notes: command.Notes);

        if (destinationLegResult.IsFailure)
        {
            return Result.Failure<TransferResult>(destinationLegResult.Error);
        }

        Transaction sourceLeg = sourceLegResult.Value;
        Transaction destinationLeg = destinationLegResult.Value;

        db.Transactions.Add(sourceLeg);
        db.Transactions.Add(destinationLeg);

        await db.SaveChangesAsync(cancellationToken);

        return new TransferResult(sourceLeg.Id, destinationLeg.Id);
    }
}
