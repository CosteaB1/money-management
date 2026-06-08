using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.UpdateTransactionCategory;

internal sealed class UpdateTransactionCategoryCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter) : ICommandHandler<UpdateTransactionCategoryCommand>
{
    public async Task<Result> Handle(UpdateTransactionCategoryCommand command, CancellationToken cancellationToken)
    {
        // The IsDeleted query filter on Transactions keeps soft-deleted rows out.
        Transaction? transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);

        if (transaction is null)
        {
            return Result.Failure(TransactionErrors.NotFound(command.Id));
        }

        if (command.CategoryId is { } categoryId)
        {
            Category? category = await db.Categories
                .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

            if (category is null)
            {
                return Result.Failure(CategoryErrors.NotFound(categoryId));
            }

            if (!IsFlowCompatible(transaction.Direction, category.Flow))
            {
                return Result.Failure(TransactionErrors.CategoryFlowMismatch);
            }
        }

        decimal? amountMdl = await fxConverter.ConvertAsync(
            transaction.Amount.Amount,
            transaction.Amount.Currency,
            ReportingCurrencies.Mdl,
            transaction.TransactionDate,
            cancellationToken);

        transaction.SetCategory(command.CategoryId, amountMdl);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static bool IsFlowCompatible(TransactionDirection direction, CategoryFlow flow) => flow switch
    {
        CategoryFlow.Both => true,
        CategoryFlow.Income => direction == TransactionDirection.Income,
        CategoryFlow.Expense => direction == TransactionDirection.Expense,
        _ => false,
    };
}
