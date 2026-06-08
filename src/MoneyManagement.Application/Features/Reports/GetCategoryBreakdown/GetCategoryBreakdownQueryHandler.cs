using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Reports.GetCategoryBreakdown;

internal sealed class GetCategoryBreakdownQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : IQueryHandler<GetCategoryBreakdownQuery, CategoryBreakdownDto>
{
    private const string UncategorizedLabel = "Uncategorized";

    public async Task<Result<CategoryBreakdownDto>> Handle(
        GetCategoryBreakdownQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From > query.To)
        {
            return Result.Failure<CategoryBreakdownDto>(
                ReportsErrors.RangeOutOfBounds("from must be on or before to."));
        }

        DateOnly from = query.From;
        DateOnly to = query.To;
        TransactionDirection direction = query.Direction;

        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => !t.IsTransfer)
            .Where(t => !t.IsAdjustment)
            .Where(t => t.Direction == direction)
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to)
            .Select(t => new
            {
                t.CategoryId,
                t.TransactionDate,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        List<Category> categories = await db.Categories
            .ToListAsync(cancellationToken);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);

        // Use Guid.Empty as the sentinel key for uncategorized rows — Dictionary
        // requires a non-nullable TKey under nullable reference types, and
        // Guid.Empty is never a valid Category.Id (always generated via v7).
        var buckets = new Dictionary<Guid, (decimal AmountMdl, int Count)>();
        bool missingFxRate = false;

        foreach (var row in rows)
        {
            decimal? mdl = await fxConverter.ConvertAsync(
                row.AmountValue,
                row.AmountCurrency,
                ReportingCurrencies.Mdl,
                row.TransactionDate,
                cancellationToken);

            if (mdl is null)
            {
                missingFxRate = true;
                continue;
            }

            Guid key = row.CategoryId ?? Guid.Empty;
            buckets.TryGetValue(key, out (decimal AmountMdl, int Count) bucket);
            buckets[key] = (bucket.AmountMdl + mdl.Value, bucket.Count + 1);
        }

        decimal totalMdl = buckets.Values.Sum(b => b.AmountMdl);

        var items = buckets
            .Select(kvp =>
            {
                Guid? categoryId = kvp.Key == Guid.Empty ? null : kvp.Key;
                string name = categoryId is { } id && categoryNames.TryGetValue(id, out string? n)
                    ? n
                    : UncategorizedLabel;

                decimal percentage = totalMdl == 0m ? 0m : kvp.Value.AmountMdl / totalMdl;
                return new CategoryBreakdownItemDto(
                    categoryId,
                    name,
                    kvp.Value.AmountMdl,
                    percentage,
                    kvp.Value.Count);
            })
            .OrderByDescending(i => i.AmountMdl)
            .ToList();

        return Result.Success(new CategoryBreakdownDto(
            from,
            to,
            direction,
            totalMdl,
            missingFxRate,
            items));
    }
}
