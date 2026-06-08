using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Common;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.GetTransactions;

internal sealed class GetTransactionsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetTransactionsQuery, PagedResult<TransactionDto>>
{
    public async Task<Result<PagedResult<TransactionDto>>> Handle(
        GetTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Transaction> transactions = db.Transactions.AsQueryable();

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

        int totalCount = await transactions.CountAsync(cancellationToken);

        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int pageNumber = Math.Max(1, query.PageNumber);

        // Materialize first - the MDL-equivalent projection needs in-memory FX
        // lookup, which can't be expressed in the EF translator.
        var rows = await transactions
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.AccountId,
                t.CategoryId,
                CategoryName = t.CategoryId == null
                    ? null
                    : db.Categories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                t.TransactionDate,
                t.Direction,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
                t.Description,
                t.Notes,
                t.OriginalAmount,
                t.OriginalCurrency,
                t.Source,
                t.ImportBatchId,
                t.IsTransfer,
                t.CounterAccountId,
                t.IsAdjustment,
            })
            .ToListAsync(cancellationToken);

        // FxRate is a small reference table - one materialization per query is
        // cheaper than issuing N converter calls. Mirrors GetAccountsQueryHandler.
        List<FxRate> rates = await db.FxRates
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<TransactionDto>(rows.Count);
        foreach (var row in rows)
        {
            decimal? amountMdl = ConvertInMemory(
                rates,
                row.AmountValue,
                row.AmountCurrency,
                ReportingCurrencies.Mdl,
                row.TransactionDate);

            result.Add(new TransactionDto(
                row.Id,
                row.AccountId,
                row.CategoryId,
                row.CategoryName,
                row.TransactionDate,
                row.Direction,
                row.AmountValue,
                row.AmountCurrency,
                amountMdl,
                row.Description,
                row.Notes,
                row.OriginalAmount,
                row.OriginalCurrency,
                row.Source,
                row.ImportBatchId,
                row.IsTransfer,
                row.CounterAccountId,
                row.IsAdjustment));
        }

        return Result.Success(new PagedResult<TransactionDto>(result, totalCount, pageNumber, pageSize));
    }

    /// <summary>
    /// Mirrors <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/>'s
    /// algorithm against an in-memory rate snapshot. Identity, direct, inverse,
    /// or <c>null</c> when no usable rate exists.
    /// </summary>
    private static decimal? ConvertInMemory(
        List<FxRate> rates,
        decimal amount,
        string fromCurrency,
        string toCurrency,
        DateOnly asOf)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.Ordinal))
        {
            return amount;
        }

        // Tie-break mirrors EfFxConverter: on a same (from, to, asOf) triple a
        // Manual row must win over BnmAuto, so AmountMdl agrees with the
        // dashboard/reports (which all go through EfFxConverter). Ordering by an
        // explicit numeric key (Manual => 0) — never by r.Source directly.
        FxRate? direct = rates
            .Where(r =>
                r.FromCurrency == fromCurrency &&
                r.ToCurrency == toCurrency &&
                r.AsOf <= asOf)
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)
            .FirstOrDefault();

        if (direct is not null)
        {
            return amount * direct.Rate;
        }

        FxRate? inverse = rates
            .Where(r =>
                r.FromCurrency == toCurrency &&
                r.ToCurrency == fromCurrency &&
                r.AsOf <= asOf)
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)
            .FirstOrDefault();

        if (inverse is not null && inverse.Rate > 0m)
        {
            return amount * (1m / inverse.Rate);
        }

        return null;
    }
}
