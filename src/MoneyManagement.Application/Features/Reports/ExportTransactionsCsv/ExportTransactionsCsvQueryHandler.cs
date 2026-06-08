using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Reports.ExportTransactionsCsv;

internal sealed class ExportTransactionsCsvQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : IQueryHandler<ExportTransactionsCsvQuery, IReadOnlyList<TransactionExportRow>>
{
    public async Task<Result<IReadOnlyList<TransactionExportRow>>> Handle(
        ExportTransactionsCsvQuery query,
        CancellationToken cancellationToken)
    {
        // Filter discipline is NOT applied automatically here — the CSV is a
        // caller-driven export, and IsTransfer / IsAdjustment are surfaced as
        // explicit query parameters so the user controls inclusion.
        IQueryable<Transaction> transactions = db.Transactions.Where(t => !t.IsDeleted);

        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(t => t.AccountId == accountId);
        }

        if (query.From is { } from)
        {
            transactions = transactions.Where(t => t.TransactionDate >= from);
        }

        if (query.To is { } to)
        {
            transactions = transactions.Where(t => t.TransactionDate <= to);
        }

        if (query.CategoryId is { } categoryId)
        {
            transactions = transactions.Where(t => t.CategoryId == categoryId);
        }

        if (query.Direction is { } direction)
        {
            transactions = transactions.Where(t => t.Direction == direction);
        }

        if (query.IsTransfer is { } isTransfer)
        {
            transactions = transactions.Where(t => t.IsTransfer == isTransfer);
        }

        if (query.IsAdjustment is { } isAdjustment)
        {
            transactions = transactions.Where(t => t.IsAdjustment == isAdjustment);
        }

        var rawRows = await transactions
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.TransactionDate,
                AccountName = db.Accounts
                    .Where(a => a.Id == t.AccountId)
                    .Select(a => a.Name)
                    .FirstOrDefault(),
                CategoryName = t.CategoryId == null
                    ? null
                    : db.Categories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                t.Direction,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
                t.Description,
                t.IsTransfer,
                t.IsAdjustment,
            })
            .ToListAsync(cancellationToken);

        var rows = new List<TransactionExportRow>(rawRows.Count);
        foreach (var row in rawRows)
        {
            decimal? amountMdl = await fxConverter.ConvertAsync(
                row.AmountValue,
                row.AmountCurrency,
                ReportingCurrencies.Mdl,
                row.TransactionDate,
                cancellationToken);

            rows.Add(new TransactionExportRow(
                row.TransactionDate,
                row.AccountName ?? string.Empty,
                row.CategoryName ?? string.Empty,
                row.Direction,
                row.AmountValue,
                row.AmountCurrency,
                amountMdl,
                row.Description,
                row.IsTransfer,
                row.IsAdjustment));
        }

        return Result.Success<IReadOnlyList<TransactionExportRow>>(rows);
    }
}
