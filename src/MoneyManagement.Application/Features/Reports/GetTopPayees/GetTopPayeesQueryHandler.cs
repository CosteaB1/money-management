using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Reports.GetTopPayees;

internal sealed class GetTopPayeesQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : IQueryHandler<GetTopPayeesQuery, IReadOnlyList<TopPayeeDto>>
{
    public const int MinLimit = 1;
    public const int MaxLimit = 50;

    public async Task<Result<IReadOnlyList<TopPayeeDto>>> Handle(
        GetTopPayeesQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From > query.To)
        {
            return Result.Failure<IReadOnlyList<TopPayeeDto>>(
                ReportsErrors.RangeOutOfBounds("from must be on or before to."));
        }

        int limit = Math.Clamp(query.Limit, MinLimit, MaxLimit);
        DateOnly from = query.From;
        DateOnly to = query.To;
        TransactionDirection direction = query.Direction;

        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => !t.IsTransfer)
            .Where(t => !t.IsAdjustment)
            .Where(t => t.Direction == direction)
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.TransactionDate,
                t.Description,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        var buckets = new Dictionary<string, (decimal AmountMdl, int Count, string Original)>(StringComparer.Ordinal);

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
                continue;
            }

            string normalized = (row.Description ?? string.Empty).Trim().ToLowerInvariant();
            if (buckets.TryGetValue(normalized, out (decimal AmountMdl, int Count, string Original) existing))
            {
                buckets[normalized] = (existing.AmountMdl + mdl.Value, existing.Count + 1, existing.Original);
            }
            else
            {
                // The first encountered row drives the display label so users
                // see whatever casing/spacing the original transaction carried.
                buckets[normalized] = (mdl.Value, 1, row.Description ?? string.Empty);
            }
        }

        IReadOnlyList<TopPayeeDto> items = buckets
            .Select(b => new TopPayeeDto(b.Key, b.Value.Original, b.Value.AmountMdl, b.Value.Count))
            .OrderByDescending(b => b.AmountMdl)
            .Take(limit)
            .ToList();

        return Result.Success(items);
    }
}
