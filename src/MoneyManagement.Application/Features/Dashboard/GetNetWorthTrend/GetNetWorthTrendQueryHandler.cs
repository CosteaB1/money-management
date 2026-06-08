using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;

/// <summary>
/// Builds a rolling N-point net-worth series.
/// <list type="bullet">
/// <item>For past months, the as-of date is the last day of that month (UTC).</item>
/// <item>For the current month, the as-of date is "now" so the latest point is live.</item>
/// <item>Each point sums every non-archived account's native balance (anchor + Σ income − Σ expense over rows ≤ asOf), FX-converted to MDL at that as-of date.</item>
/// </list>
/// Mirrors <c>GetAccountsQueryHandler</c>'s balance arithmetic — non-deleted
/// transactions only; transfers, adjustments and fees all contribute.
/// </summary>
internal sealed class GetNetWorthTrendQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock)
    : IQueryHandler<GetNetWorthTrendQuery, IReadOnlyList<NetWorthTrendPointDto>>
{
    public const int MinMonths = 1;
    public const int MaxMonths = 24;

    public async Task<Result<IReadOnlyList<NetWorthTrendPointDto>>> Handle(
        GetNetWorthTrendQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Months < MinMonths || query.Months > MaxMonths)
        {
            return Result.Failure<IReadOnlyList<NetWorthTrendPointDto>>(
                DashboardErrors.MonthsOutOfRange(MinMonths, MaxMonths));
        }

        DateTime now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // The full per-account, per-direction transaction sum, partitioned by
        // transaction date. We materialize all non-deleted rows once and slice
        // them in memory for each as-of date — far cheaper than running N
        // GROUP BY queries against Postgres, and N is bounded at 24.
        var txRows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Select(t => new
            {
                t.AccountId,
                t.Direction,
                t.TransactionDate,
                AmountValue = t.Amount.Amount,
            })
            .ToListAsync(cancellationToken);

        // Non-archived accounts only — mirrors what the dashboard caller
        // expects (archived accounts hide from the dashboard per WIKI.md).
        List<Account> accounts = await db.Accounts
            .Where(a => !a.IsArchived)
            .ToListAsync(cancellationToken);

        // Build the list of as-of dates, oldest first.
        //
        // Convention: the LAST point is always "now" (the live current month).
        // The earlier (months - 1) points are previous month-ends, walking
        // backwards. For months = 1 the result is a single live point.
        var asOfDates = new List<DateOnly>(query.Months);
        var currentMonthFirst = new DateOnly(today.Year, today.Month, 1);
        for (int offset = query.Months - 1; offset >= 1; offset--)
        {
            // Last day of the month that is `offset` months before the current
            // month. Computed as (firstOfThatMonth.AddMonths(1) - 1 day).
            DateOnly firstOfThatMonth = currentMonthFirst.AddMonths(-offset);
            DateOnly lastOfThatMonth = firstOfThatMonth.AddMonths(1).AddDays(-1);
            asOfDates.Add(lastOfThatMonth);
        }
        asOfDates.Add(today);

        var points = new List<NetWorthTrendPointDto>(asOfDates.Count);

        for (int i = 0; i < asOfDates.Count; i++)
        {
            DateOnly asOf = asOfDates[i];

            decimal netWorthMdl = 0m;
            bool missing = false;

            foreach (Account account in accounts)
            {
                // The account contributes nothing before it existed.
                if (account.OpeningDate > asOf)
                {
                    continue;
                }

                decimal income = 0m;
                decimal expense = 0m;
                foreach (var t in txRows)
                {
                    if (t.AccountId != account.Id)
                    {
                        continue;
                    }
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

                decimal nativeBalance = account.Balance.Amount + income - expense;

                decimal? converted = await fxConverter.ConvertAsync(
                    nativeBalance,
                    account.Balance.Currency,
                    ReportingCurrencies.Mdl,
                    asOf,
                    cancellationToken);

                if (converted is null)
                {
                    missing = true;
                    continue;
                }

                netWorthMdl += converted.Value;
            }

            // Point label: for the live point use today's month; for past
            // points use the asOf's own month — they're identical when the
            // current month's last point is "today" but stays robust if we
            // ever switch the live anchor.
            string monthLabel = asOf.ToString("yyyy-MM", CultureInfo.InvariantCulture);

            points.Add(new NetWorthTrendPointDto(monthLabel, netWorthMdl, missing));
        }

        return Result.Success<IReadOnlyList<NetWorthTrendPointDto>>(points);
    }
}
