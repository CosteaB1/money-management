using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Reports.GetBalanceOverTime;

internal sealed class GetBalanceOverTimeQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : IQueryHandler<GetBalanceOverTimeQuery, IReadOnlyList<BalancePointDto>>
{
    public const int MaxDailyDays = 366 * 3;

    public async Task<Result<IReadOnlyList<BalancePointDto>>> Handle(
        GetBalanceOverTimeQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From > query.To)
        {
            return Result.Failure<IReadOnlyList<BalancePointDto>>(
                ReportsErrors.RangeOutOfBounds("from must be on or before to."));
        }

        if (query.Interval == BalanceInterval.Daily)
        {
            int days = query.To.DayNumber - query.From.DayNumber + 1;
            if (days > MaxDailyDays)
            {
                return Result.Failure<IReadOnlyList<BalancePointDto>>(
                    ReportsErrors.IntervalTooFine(
                        $"Daily interval supports at most {MaxDailyDays} days; widen the range or coarsen the interval."));
            }
        }

        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == query.AccountId, cancellationToken);

        if (account is null || account.IsArchived)
        {
            return Result.Failure<IReadOnlyList<BalancePointDto>>(
                AccountErrors.NotFound(query.AccountId));
        }

        // Per-account balance trend is balance arithmetic, NOT a P&L slice:
        // transfers and adjustments DO move the native balance, so we keep them
        // in the sum. The filter discipline used by income/expense aggregates
        // (drop !IsTransfer && !IsAdjustment) deliberately does NOT apply here.
        var txRows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => t.AccountId == account.Id)
            .Select(t => new
            {
                t.TransactionDate,
                t.Direction,
                AmountValue = t.Amount.Amount,
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<DateOnly> asOfDates = BuildAsOfDates(query.From, query.To, query.Interval);
        var points = new List<BalancePointDto>(asOfDates.Count);

        foreach (DateOnly asOf in asOfDates)
        {
            decimal income = 0m;
            decimal expense = 0m;
            foreach (var t in txRows)
            {
                if (t.TransactionDate > asOf)
                {
                    continue;
                }

                if (t.Direction == TransactionDirection.Income)
                {
                    income += t.AmountValue;
                }
                else
                {
                    expense += t.AmountValue;
                }
            }

            decimal balance = account.Balance.Amount + income - expense;

            decimal? balanceMdl = await fxConverter.ConvertAsync(
                balance,
                account.Balance.Currency,
                ReportingCurrencies.Mdl,
                asOf,
                cancellationToken);

            points.Add(new BalancePointDto(asOf, balance, balanceMdl, balanceMdl is null));
        }

        return Result.Success<IReadOnlyList<BalancePointDto>>(points);
    }

    private static IReadOnlyList<DateOnly> BuildAsOfDates(DateOnly from, DateOnly to, BalanceInterval interval)
    {
        var dates = new List<DateOnly>();
        switch (interval)
        {
            case BalanceInterval.Daily:
                for (DateOnly d = from; d <= to; d = d.AddDays(1))
                {
                    dates.Add(d);
                }
                break;

            case BalanceInterval.Weekly:
                // Step in 7-day increments anchored at `from`; clamp the final
                // point to `to` so the trend always closes on the caller's
                // requested end date instead of stopping a few days short.
                for (DateOnly d = from; d <= to; d = d.AddDays(7))
                {
                    dates.Add(d);
                }
                if (dates.Count == 0 || dates[^1] != to)
                {
                    dates.Add(to);
                }
                break;

            case BalanceInterval.Monthly:
            default:
                // One point per month-end inside the range, plus a final point
                // at `to` (the caller cares about "where am I now in this range").
                var cursorMonth = new DateOnly(from.Year, from.Month, 1);
                while (cursorMonth <= to)
                {
                    DateOnly monthEnd = cursorMonth.AddMonths(1).AddDays(-1);
                    DateOnly clamped = monthEnd > to ? to : monthEnd;
                    if (clamped >= from)
                    {
                        dates.Add(clamped);
                    }
                    cursorMonth = cursorMonth.AddMonths(1);
                }

                if (dates.Count == 0 || dates[^1] != to)
                {
                    dates.Add(to);
                }
                break;
        }

        return dates;
    }
}
