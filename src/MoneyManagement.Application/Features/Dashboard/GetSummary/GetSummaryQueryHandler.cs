using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Dashboard.GetSummary;

/// <summary>
/// Aggregates a single calendar month's income/expense to MDL.
/// Mirrors <c>GetTransactionsQueryHandler</c>'s <c>AmountMdl</c> convention:
/// each row is converted at its own transaction date, not at "now". Transfers
/// and balance adjustments are filtered out because they are not real P&amp;L.
/// </summary>
internal sealed class GetSummaryQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock)
    : IQueryHandler<GetSummaryQuery, DashboardSummaryDto>
{
    public async Task<Result<DashboardSummaryDto>> Handle(
        GetSummaryQuery query,
        CancellationToken cancellationToken)
    {
        // Default to the current UTC month when the caller didn't specify one.
        DateTime now = clock.UtcNow;
        DateOnly anchor = query.Month ?? new DateOnly(now.Year, now.Month, 1);

        var windowStart = new DateOnly(anchor.Year, anchor.Month, 1);
        DateOnly windowEnd = windowStart.AddMonths(1);

        // The `is_deleted` global query filter excludes soft-deleted rows under
        // EF Core. The explicit `!t.IsDeleted` predicate is defense-in-depth so
        // unit tests (which bypass model configuration) behave identically.
        // Transfer/adjustment exclusion is the slice's whole reason to exist —
        // see the "Known rough edges" note in BACKEND.md.
        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => !t.IsTransfer)
            .Where(t => !t.IsAdjustment)
            .Where(t => t.TransactionDate >= windowStart && t.TransactionDate < windowEnd)
            .Select(t => new
            {
                t.TransactionDate,
                t.Direction,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        decimal income = 0m;
        decimal expense = 0m;
        int count = 0;
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
                // Unconvertible rows are surfaced via the flag and omitted from
                // totals — mirrors the account-list endpoint's null BalanceMdl
                // semantics ("no implicit 1:1 fallback").
                missingFxRate = true;
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

            count++;
        }

        decimal net = income - expense;
        decimal savingsRate = income == 0m ? 0m : net / income;

        string monthLabel = windowStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        return Result.Success(new DashboardSummaryDto(
            monthLabel,
            income,
            expense,
            net,
            savingsRate,
            count,
            missingFxRate));
    }
}
