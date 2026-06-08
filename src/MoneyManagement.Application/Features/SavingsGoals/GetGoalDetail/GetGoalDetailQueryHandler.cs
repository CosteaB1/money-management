using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.SavingsGoals.GetGoalDetail;

internal sealed class GetGoalDetailQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock)
    : IQueryHandler<GetGoalDetailQuery, GoalDetailDto>
{
    // Average days-per-month over the long run (365.25 / 12). Same constant
    // used inside GoalProjection's pace math — kept here to bridge to the
    // window/projection calculations the projection helper doesn't cover.
    private const decimal DaysPerMonth = 30.4375m;

    // Trailing window for the pace calculation. 90 days is the smallest window
    // that smooths out a single fat contribution (people lump-sum every paycheck)
    // while still reacting to a quarter-on-quarter trend change.
    private const int PaceWindowDays = 90;

    // Clamp absurdly slow paces so projected dates don't overflow. If the
    // trailing average suggests a goal will take >50 years, we treat it as
    // "not projectable" rather than rendering a year 2070+ date.
    private const decimal MaxMonthsToAchieve = 600m;

    public async Task<Result<GoalDetailDto>> Handle(
        GetGoalDetailQuery query,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so archived goals stay reachable here — the goal
        // detail page is the user's drill-in for both active and archived goals.
        SavingsGoal? goal = await db.SavingsGoals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == query.Id, cancellationToken);

        if (goal is null)
        {
            return Result.Failure<GoalDetailDto>(SavingsGoalErrors.NotFound(query.Id));
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var createdOn = DateOnly.FromDateTime(goal.CreatedAt);

        decimal saved;
        string? linkedAccountName = null;
        bool missingFxRate = false;
        Account? linkedAccount = null;

        IReadOnlyList<GoalContributionDto> contributions;
        IReadOnlyList<GoalSavedPointDto> savedHistory;
        decimal? avgMonthlyContribution;

        if (goal.LinkedAccountId is Guid linkedId)
        {
            linkedAccount = await db.Accounts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == linkedId, cancellationToken);

            if (linkedAccount is null)
            {
                // Dangling FK — the Restrict policy makes this unreachable in
                // production but the read path stays defensive.
                saved = 0m;
                contributions = [];
                savedHistory = BuildEmptySavedHistory(createdOn, today, saved);
                avgMonthlyContribution = null;
            }
            else
            {
                linkedAccountName = linkedAccount.Name;
                (saved, missingFxRate) = await ComputeLinkedSavedAsync(
                    linkedAccount, today, cancellationToken);

                (contributions, bool linkedMissing) = await BuildLinkedContributionsAsync(
                    linkedAccount, cancellationToken);
                if (linkedMissing)
                {
                    missingFxRate = true;
                }

                savedHistory = await BuildLinkedSavedHistoryAsync(
                    linkedAccount, createdOn, today, cancellationToken);

                avgMonthlyContribution = await ComputeLinkedAvgMonthlyAsync(
                    linkedAccount, saved, today, createdOn, cancellationToken);
            }
        }
        else
        {
            saved = goal.ManualSavedAmount?.Amount ?? 0m;

            List<SavingsGoalContribution> rows = await db.SavingsGoalContributions
                .Where(c => c.GoalId == goal.Id)
                .OrderByDescending(c => c.OccurredOn)
                .ToListAsync(cancellationToken);

            contributions = rows
                .Select(c => new GoalContributionDto(
                    c.Id,
                    c.Amount.Amount,
                    c.OccurredOn,
                    c.Notes,
                    GoalContributionSource.Manual))
                .ToList();

            savedHistory = BuildManualSavedHistory(rows, createdOn, today, saved);
            avgMonthlyContribution = ComputeManualAvgMonthly(rows, today, createdOn);
        }

        GoalProjection.Projection projection = GoalProjection.Project(goal, saved, today);

        GoalPaceStatsDto pace = BuildPaceStats(
            avgMonthlyContribution,
            saved,
            goal.TargetAmount.Amount,
            today);

        var dto = new GoalDetailDto(
            goal.Id,
            goal.Name,
            goal.TargetAmount.Amount,
            goal.TargetDate,
            goal.LinkedAccountId,
            linkedAccountName,
            saved,
            projection.Remaining,
            projection.ProgressPercent,
            projection.Status,
            projection.RequiredMonthlyContribution,
            IsLinkedMode: goal.LinkedAccountId is not null,
            MissingFxRate: missingFxRate,
            CreatedOn: createdOn,
            IsArchived: goal.IsArchived,
            Pace: pace,
            Contributions: contributions,
            SavedHistory: savedHistory);

        return Result.Success(dto);
    }

    private async Task<(decimal Saved, bool MissingFxRate)> ComputeLinkedSavedAsync(
        Account account,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var rows = await db.Transactions
            .Where(t => !t.IsDeleted && t.AccountId == account.Id)
            .Select(t => new { t.Direction, AmountValue = t.Amount.Amount })
            .ToListAsync(cancellationToken);

        decimal income = 0m;
        decimal expense = 0m;
        foreach (var r in rows)
        {
            if (r.Direction == TransactionDirection.Income)
            {
                income += r.AmountValue;
            }
            else
            {
                expense += r.AmountValue;
            }
        }

        decimal native = account.Balance.Amount + income - expense;
        decimal? mdl = await fxConverter.ConvertAsync(
            native,
            account.Balance.Currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        return mdl is null ? (0m, true) : (mdl.Value, false);
    }

    private async Task<(IReadOnlyList<GoalContributionDto> Rows, bool MissingFxRate)> BuildLinkedContributionsAsync(
        Account account,
        CancellationToken cancellationToken)
    {
        var rows = await db.Transactions
            .Where(t => !t.IsDeleted && t.AccountId == account.Id)
            .Select(t => new
            {
                t.Id,
                t.TransactionDate,
                t.Direction,
                t.Description,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        var result = new List<GoalContributionDto>(rows.Count);
        bool missingFxRate = false;

        foreach (var r in rows)
        {
            decimal? mdl = await fxConverter.ConvertAsync(
                r.AmountValue,
                r.AmountCurrency,
                ReportingCurrencies.Mdl,
                r.TransactionDate,
                cancellationToken);

            if (mdl is null)
            {
                // Drop rows we can't price into MDL but flag the page so the
                // user is told the list is incomplete. Mirrors GetSummary's
                // policy of surfacing the gap instead of silently zeroing.
                missingFxRate = true;
                continue;
            }

            decimal signed = r.Direction == TransactionDirection.Income ? mdl.Value : -mdl.Value;
            result.Add(new GoalContributionDto(
                Id: null,
                Amount: signed,
                OccurredOn: r.TransactionDate,
                Notes: r.Description,
                Source: GoalContributionSource.LinkedAccountTransaction));
        }

        result.Sort((a, b) => b.OccurredOn.CompareTo(a.OccurredOn));
        return (result, missingFxRate);
    }

    private async Task<IReadOnlyList<GoalSavedPointDto>> BuildLinkedSavedHistoryAsync(
        Account account,
        DateOnly createdOn,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var txRows = await db.Transactions
            .Where(t => !t.IsDeleted && t.AccountId == account.Id)
            .Select(t => new
            {
                t.TransactionDate,
                t.Direction,
                AmountValue = t.Amount.Amount,
            })
            .ToListAsync(cancellationToken);

        // Cap the window at 12 months — the goal-detail chart shows a
        // rolling year, not the full history of the linked account.
        DateOnly windowStart = createdOn > today.AddMonths(-11) ? createdOn : today.AddMonths(-11);
        IReadOnlyList<DateOnly> asOfDates = BuildMonthEndPoints(windowStart, today);

        var points = new List<GoalSavedPointDto>(asOfDates.Count);
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

            decimal native = account.Balance.Amount + income - expense;
            decimal? mdl = await fxConverter.ConvertAsync(
                native,
                account.Balance.Currency,
                ReportingCurrencies.Mdl,
                asOf,
                cancellationToken);

            points.Add(new GoalSavedPointDto(asOf, mdl ?? 0m));
        }

        return DedupeByAsOf(points);
    }

    private async Task<decimal?> ComputeLinkedAvgMonthlyAsync(
        Account account,
        decimal savedToday,
        DateOnly today,
        DateOnly createdOn,
        CancellationToken cancellationToken)
    {
        DateOnly windowStart = today.AddDays(-PaceWindowDays);
        if (windowStart < createdOn)
        {
            windowStart = createdOn;
        }

        decimal daysInWindow = today.DayNumber - windowStart.DayNumber;
        if (daysInWindow <= 0m)
        {
            return null;
        }

        decimal monthsInWindow = daysInWindow / DaysPerMonth;
        if (monthsInWindow < 1m)
        {
            return null;
        }

        var txRows = await db.Transactions
            .Where(t => !t.IsDeleted && t.AccountId == account.Id)
            .Where(t => t.TransactionDate <= windowStart)
            .Select(t => new { t.Direction, AmountValue = t.Amount.Amount })
            .ToListAsync(cancellationToken);

        decimal income = 0m;
        decimal expense = 0m;
        foreach (var r in txRows)
        {
            if (r.Direction == TransactionDirection.Income)
            {
                income += r.AmountValue;
            }
            else
            {
                expense += r.AmountValue;
            }
        }

        decimal nativeAtStart = account.Balance.Amount + income - expense;
        decimal? mdlAtStart = await fxConverter.ConvertAsync(
            nativeAtStart,
            account.Balance.Currency,
            ReportingCurrencies.Mdl,
            windowStart,
            cancellationToken);

        if (mdlAtStart is null)
        {
            return null;
        }

        return (savedToday - mdlAtStart.Value) / monthsInWindow;
    }

    private static decimal? ComputeManualAvgMonthly(
        IReadOnlyList<SavingsGoalContribution> rows,
        DateOnly today,
        DateOnly createdOn)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        DateOnly windowStart = today.AddDays(-PaceWindowDays);
        if (windowStart < createdOn)
        {
            windowStart = createdOn;
        }

        decimal daysInWindow = today.DayNumber - windowStart.DayNumber;
        if (daysInWindow <= 0m)
        {
            return null;
        }

        decimal monthsInWindow = daysInWindow / DaysPerMonth;
        if (monthsInWindow < 1m)
        {
            return null;
        }

        decimal sum = 0m;
        foreach (SavingsGoalContribution c in rows)
        {
            if (c.OccurredOn >= windowStart && c.OccurredOn <= today)
            {
                sum += c.Amount.Amount;
            }
        }

        return sum / monthsInWindow;
    }

    private static IReadOnlyList<GoalSavedPointDto> BuildManualSavedHistory(
        IReadOnlyList<SavingsGoalContribution> rows,
        DateOnly createdOn,
        DateOnly today,
        decimal savedToday)
    {
        // Even a brand-new goal renders two points so the chart isn't a
        // single dot — created-on at zero, today at the current value.
        if (rows.Count == 0)
        {
            return BuildEmptySavedHistory(createdOn, today, savedToday);
        }

        var ordered = rows.OrderBy(r => r.OccurredOn).ToList();

        IReadOnlyList<DateOnly> monthlyEnds = BuildMonthEndPoints(createdOn, today);
        var points = new List<GoalSavedPointDto>(monthlyEnds.Count);

        foreach (DateOnly asOf in monthlyEnds)
        {
            decimal running = 0m;
            foreach (SavingsGoalContribution c in ordered)
            {
                if (c.OccurredOn <= asOf)
                {
                    running += c.Amount.Amount;
                }
            }

            points.Add(new GoalSavedPointDto(asOf, running));
        }

        // Guarantee the series always closes on today's value so callers don't
        // see a stale month-end for the in-flight current month if the latest
        // contribution lands mid-month.
        if (points.Count == 0 || points[^1].AsOf != today)
        {
            points.Add(new GoalSavedPointDto(today, savedToday));
        }
        else
        {
            points[^1] = new GoalSavedPointDto(today, savedToday);
        }

        // A single point would render as a lone dot. Prepend a created-on
        // baseline so the chart draws a line — but only when created-on is
        // strictly earlier than that point, otherwise the two would collide on
        // the same x (goal created today + contribution dated today).
        if (points.Count == 1 && createdOn < points[0].AsOf)
        {
            points.Insert(0, new GoalSavedPointDto(createdOn, 0m));
        }

        return DedupeByAsOf(points);
    }

    private static IReadOnlyList<GoalSavedPointDto> BuildEmptySavedHistory(
        DateOnly createdOn, DateOnly today, decimal saved)
    {
        // A goal created today collapses to a single point — a created-on
        // baseline would share today's x and produce a duplicate AsOf.
        if (createdOn >= today)
        {
            return [new GoalSavedPointDto(today, saved)];
        }

        return
        [
            new GoalSavedPointDto(createdOn, 0m),
            new GoalSavedPointDto(today, saved),
        ];
    }

    /// <summary>
    /// Collapse consecutive points that share the same <see cref="GoalSavedPointDto.AsOf"/>,
    /// keeping the LAST (latest cumulative) value. The saved-history series is
    /// always built in non-decreasing <c>AsOf</c> order, so consecutive-only
    /// dedupe is sufficient to guarantee strictly-increasing x values — the
    /// frontend chart keys on <c>AsOf</c> and breaks on duplicates.
    /// </summary>
    private static IReadOnlyList<GoalSavedPointDto> DedupeByAsOf(
        IReadOnlyList<GoalSavedPointDto> points)
    {
        if (points.Count <= 1)
        {
            return points;
        }

        var result = new List<GoalSavedPointDto>(points.Count);
        foreach (GoalSavedPointDto point in points)
        {
            if (result.Count > 0 && result[^1].AsOf == point.AsOf)
            {
                // Same x as the prior point — overwrite with the later value so
                // the kept point carries the latest cumulative total.
                result[^1] = point;
            }
            else
            {
                result.Add(point);
            }
        }

        return result;
    }

    private GoalPaceStatsDto BuildPaceStats(
        decimal? avgMonthly,
        decimal saved,
        decimal target,
        DateOnly today)
    {
        if (saved >= target)
        {
            // Achieved goals don't need a forward projection. We still surface
            // the trailing average so the UI can render the historical pace.
            return new GoalPaceStatsDto(
                AvgMonthlyContribution: avgMonthly,
                ProjectedCompletionDate: null,
                MonthsToAchieveAtPace: 0m);
        }

        if (avgMonthly is not decimal avg || avg <= 0m)
        {
            return new GoalPaceStatsDto(avgMonthly, null, null);
        }

        decimal monthsToAchieve = (target - saved) / avg;
        if (monthsToAchieve <= 0m)
        {
            return new GoalPaceStatsDto(avgMonthly, null, null);
        }

        if (monthsToAchieve > MaxMonthsToAchieve)
        {
            monthsToAchieve = MaxMonthsToAchieve;
        }

        int daysOut = (int)(monthsToAchieve * DaysPerMonth);
        DateOnly projected = today.AddDays(daysOut);

        return new GoalPaceStatsDto(avgMonthly, projected, monthsToAchieve);
    }

    /// <summary>
    /// Build month-end as-of points from <paramref name="from"/> through
    /// <paramref name="to"/>, replacing the trailing month-end with
    /// <paramref name="to"/> itself so the latest point closes on "today"
    /// rather than the prior month-end. Mirrors the convention used by the
    /// balance-over-time report's monthly bucketing.
    /// </summary>
    private static IReadOnlyList<DateOnly> BuildMonthEndPoints(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        if (to < from)
        {
            return dates;
        }

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

        return dates;
    }
}
