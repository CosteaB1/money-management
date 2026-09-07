using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;

/// <summary>
/// Builds a rolling N-point net-worth series.
/// <list type="bullet">
/// <item>For past months, the as-of date is the last day of that month (UTC).</item>
/// <item>For the current month, the as-of date is "now" so the latest point is live.</item>
/// <item>Each point sums every non-archived account's native balance (anchor + Σ income − Σ expense over rows ≤ asOf), FX-converted to MDL at that as-of date.</item>
/// <item>Each point then deducts the external claims outstanding at that same as-of date, so borrowed money never reads as wealth.</item>
/// <item>Each account contributes only the user's SHARE of its balance at that as-of date (see <see cref="IAccountOwnershipSource"/>); outside capital is never counted as wealth, so nothing has to subtract it back out.</item>
/// </list>
/// Mirrors <c>GetAccountsQueryHandler</c>'s balance arithmetic — non-deleted
/// transactions only; transfers, adjustments and fees all contribute.
/// </summary>
internal sealed class GetNetWorthTrendQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IExternalClaimSource claimSource,
    IEnumerable<IAccountOwnershipSource> ownershipSources,
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

        // The full per-account, per-direction transaction history. We
        // materialize all non-deleted rows once and slice them in memory for
        // each as-of date — far cheaper than running N GROUP BY queries against
        // Postgres, and N is bounded at 24.
        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(db, cancellationToken);

        // Non-archived accounts only — mirrors what the dashboard caller
        // expects (archived accounts hide from the dashboard per WIKI.md).
        List<Account> accounts = await db.Accounts
            .Where(a => !a.IsArchived)
            .ToListAsync(cancellationToken);

        // Same one-shot rule for the claims: their whole settlement history is
        // fetched once and re-sliced per point. A per-point query would turn one
        // round-trip into 24.
        IReadOnlyList<ExternalClaim> claims = await claimSource.GetHistoryAsync(cancellationToken);

        // Same one-shot rule again for the ownership curves: loaded once here,
        // then sliced per point. With no source registered this is an empty
        // lookup that answers 1m for every account, so the series is unchanged.
        AccountOwnershipLedger ownership =
            await AccountOwnershipLedger.LoadAsync(ownershipSources, cancellationToken);

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

                decimal nativeBalance = ledger.NativeBalanceAsOf(account, asOf);

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

                // The point's OWN date, never today's — an account diluted in
                // May must still read as wholly owned on the March point, the
                // same discipline the FX conversion above follows.
                netWorthMdl += converted.Value * ownership.OwnedFractionAsOf(account.Id, asOf);
            }

            // Now net out the obligations. OutstandingAsOf is the claim-side
            // equivalent of the opening-date guard above: a claim that did not
            // exist yet on `asOf`, or was fully settled by then, returns a
            // non-positive figure and drops out. Payments made AFTER the point
            // deliberately don't shrink it.
            //
            // Conversion happens at the point's OWN date, never at today's rate
            // — the same discipline the account leg above follows.
            foreach (ExternalClaim claim in claims)
            {
                decimal outstanding = claim.OutstandingAsOf(asOf);
                if (outstanding <= 0m)
                {
                    continue;
                }

                decimal? convertedClaim = await fxConverter.ConvertAsync(
                    outstanding,
                    claim.Currency,
                    ReportingCurrencies.Mdl,
                    asOf,
                    cancellationToken);

                if (convertedClaim is null)
                {
                    missing = true;
                    continue;
                }

                netWorthMdl += claim.Side == ExternalClaimSide.ReducesNetWorth
                    ? -convertedClaim.Value
                    : convertedClaim.Value;
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
