using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Reports.GetMonthlySummary;

/// <summary>
/// Builds a multi-month income/expense series. Each point applies the same
/// per-row FX conversion as
/// <see cref="MoneyManagement.Application.Features.Dashboard.GetSummary.GetSummaryQueryHandler"/>:
/// transactions are converted at their own transaction date, transfers and
/// adjustments are filtered out (they aren't real P&amp;L).
/// </summary>
internal sealed class GetMonthlySummaryQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock)
    : IQueryHandler<GetMonthlySummaryQuery, IReadOnlyList<MonthlySummaryPointDto>>
{
    public const int MaxMonthSpan = 24;

    public async Task<Result<IReadOnlyList<MonthlySummaryPointDto>>> Handle(
        GetMonthlySummaryQuery query,
        CancellationToken cancellationToken)
    {
        DateTime now = clock.UtcNow;
        var currentMonth = new DateOnly(now.Year, now.Month, 1);

        // Default window: trailing 12 months ending at the current UTC month.
        DateOnly fromMonth = query.From is { } f ? new DateOnly(f.Year, f.Month, 1) : currentMonth.AddMonths(-11);
        DateOnly toMonth = query.To is { } t ? new DateOnly(t.Year, t.Month, 1) : currentMonth;

        if (fromMonth > toMonth)
        {
            return Result.Failure<IReadOnlyList<MonthlySummaryPointDto>>(
                ReportsErrors.RangeOutOfBounds("from must be on or before to."));
        }

        int monthSpan = (toMonth.Year - fromMonth.Year) * 12 + (toMonth.Month - fromMonth.Month) + 1;
        if (monthSpan > MaxMonthSpan)
        {
            return Result.Failure<IReadOnlyList<MonthlySummaryPointDto>>(
                ReportsErrors.RangeOutOfBounds($"Range must not exceed {MaxMonthSpan} months."));
        }

        DateOnly windowStart = fromMonth;
        DateOnly windowEndExclusive = toMonth.AddMonths(1);

        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => !t.IsTransfer)
            .Where(t => !t.IsAdjustment)
            .Where(t => t.TransactionDate >= windowStart && t.TransactionDate < windowEndExclusive)
            .Select(t => new
            {
                t.TransactionDate,
                t.Direction,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        // Pre-seed a slot per month so zero-activity months still appear in the
        // series (clients chart it directly — missing months would look like a
        // gap or get auto-skipped).
        var slots = new Dictionary<DateOnly, (decimal Income, decimal Expense, int Count, bool Missing)>(monthSpan);
        for (int i = 0; i < monthSpan; i++)
        {
            slots[fromMonth.AddMonths(i)] = (0m, 0m, 0, false);
        }

        foreach (var row in rows)
        {
            decimal? mdl = await fxConverter.ConvertAsync(
                row.AmountValue,
                row.AmountCurrency,
                ReportingCurrencies.Mdl,
                row.TransactionDate,
                cancellationToken);

            var slotKey = new DateOnly(row.TransactionDate.Year, row.TransactionDate.Month, 1);
            (decimal income, decimal expense, int count, bool missing) = slots[slotKey];

            if (mdl is null)
            {
                slots[slotKey] = (income, expense, count, true);
                continue;
            }

            if (row.Direction == TransactionDirection.Income)
            {
                income += mdl.Value;
            }
            else
            {
                expense += mdl.Value;
            }

            slots[slotKey] = (income, expense, count + 1, missing);
        }

        var points = new List<MonthlySummaryPointDto>(monthSpan);
        for (int i = 0; i < monthSpan; i++)
        {
            DateOnly slotKey = fromMonth.AddMonths(i);
            (decimal income, decimal expense, int count, bool missing) = slots[slotKey];
            decimal net = income - expense;
            decimal savingsRate = income == 0m ? 0m : net / income;
            points.Add(new MonthlySummaryPointDto(
                slotKey.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                income,
                expense,
                net,
                savingsRate,
                count,
                missing));
        }

        return Result.Success<IReadOnlyList<MonthlySummaryPointDto>>(points);
    }
}
