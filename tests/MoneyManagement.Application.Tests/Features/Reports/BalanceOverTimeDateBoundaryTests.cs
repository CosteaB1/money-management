using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Reports.GetBalanceOverTime;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Reports;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): probes the month-end / week-stride
/// boundary builder in GetBalanceOverTime — leap-year February, December→January
/// rollover, the weekly clamp landing exactly on `to`, and the daily-interval
/// cap at exactly the 3-year guard (must pass at the limit, fail one past it).
/// </summary>
public sealed class BalanceOverTimeDateBoundaryTests
{
    private static Account NewAccount() =>
        Account.Create("Cash", AccountType.Cash, new Money(0m, "MDL"), new DateOnly(2019, 1, 1), notes: null).Value;

    private static GetBalanceOverTimeQueryHandler Handler(Account acct) =>
        new(FakeApplicationDbContext.Create(accounts: [acct]), FakeFxConverter.Identity());

    [Fact]
    public async Task Monthly_LeapYearFebruary_MonthEndIsFeb29()
    {
        Account acct = NewAccount();
        // 2024 is a leap year; the Feb month-end point must be Feb 29, not Feb 28.
        Result<IReadOnlyList<BalancePointDto>> result = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, new DateOnly(2024, 2, 1), new DateOnly(2024, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.Value.Select(p => p.AsOf).Should().Contain(new DateOnly(2024, 2, 29));
    }

    [Fact]
    public async Task Monthly_NonLeapYearFebruary_MonthEndIsFeb28()
    {
        Account acct = NewAccount();
        Result<IReadOnlyList<BalancePointDto>> result = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        // Feb 29 2025 isn't even a representable date, so just assert the
        // builder landed the non-leap-year month-end on Feb 28.
        result.Value.Select(p => p.AsOf).Should().Contain(new DateOnly(2025, 2, 28));
    }

    [Fact]
    public async Task Monthly_DecemberToJanuaryRollover_BothMonthEndsPresent()
    {
        Account acct = NewAccount();
        Result<IReadOnlyList<BalancePointDto>> result = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.Value.Select(p => p.AsOf).Should().Contain(new DateOnly(2025, 12, 31));
        result.Value.Select(p => p.AsOf).Should().Contain(new DateOnly(2026, 1, 31));
    }

    [Fact]
    public async Task Monthly_FinalPointAlwaysClampsToRequestedEnd()
    {
        Account acct = NewAccount();
        // `to` is mid-month; the last emitted point must be exactly `to`, not the
        // month-end that overshoots it.
        var to = new DateOnly(2026, 3, 15);
        Result<IReadOnlyList<BalancePointDto>> result = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, new DateOnly(2026, 1, 1), to, BalanceInterval.Monthly),
            CancellationToken.None);

        result.Value[^1].AsOf.Should().Be(to);
        result.Value.Select(p => p.AsOf).Should().NotContain(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task Weekly_FinalPointClampsToRequestedEnd_WhenStrideOvershoots()
    {
        Account acct = NewAccount();
        // 7-day stride from Jan 1 hits Jan 1, 8, 15; `to` = Jan 20 is not on the
        // stride, so the builder must append it as the closing point.
        var to = new DateOnly(2026, 1, 20);
        Result<IReadOnlyList<BalancePointDto>> result = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, new DateOnly(2026, 1, 1), to, BalanceInterval.Weekly),
            CancellationToken.None);

        result.Value[^1].AsOf.Should().Be(to);
        result.Value.Select(p => p.AsOf).Should().Contain(new DateOnly(2026, 1, 1));
        result.Value.Select(p => p.AsOf).Should().Contain(new DateOnly(2026, 1, 15));
    }

    [Fact]
    public async Task Weekly_StrideLandsExactlyOnTo_NoDuplicateClosingPoint()
    {
        Account acct = NewAccount();
        // from Jan 1 + 14 days = Jan 15 exactly == to. No duplicate point.
        var to = new DateOnly(2026, 1, 15);
        Result<IReadOnlyList<BalancePointDto>> result = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, new DateOnly(2026, 1, 1), to, BalanceInterval.Weekly),
            CancellationToken.None);

        result.Value.Select(p => p.AsOf).Should().BeEquivalentTo(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8), new DateOnly(2026, 1, 15)]);
        result.Value.Count(p => p.AsOf == to).Should().Be(1);
    }

    [Fact]
    public async Task Daily_ExactlyAtCap_Succeeds_OnePastCap_Fails()
    {
        Account acct = NewAccount();
        const int cap = 366 * 3; // GetBalanceOverTimeQueryHandler.MaxDailyDays

        var from = new DateOnly(2024, 1, 1);
        // days = To.DayNumber - From.DayNumber + 1 == cap  → To = from + (cap-1)
        DateOnly toAtCap = from.AddDays(cap - 1);

        Result<IReadOnlyList<BalancePointDto>> atCap = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, from, toAtCap, BalanceInterval.Daily),
            CancellationToken.None);
        atCap.IsSuccess.Should().BeTrue();
        atCap.Value.Should().HaveCount(cap);

        Result<IReadOnlyList<BalancePointDto>> onePast = await Handler(acct).Handle(
            new GetBalanceOverTimeQuery(acct.Id, from, toAtCap.AddDays(1), BalanceInterval.Daily),
            CancellationToken.None);
        onePast.IsFailure.Should().BeTrue();
        onePast.Error.Code.Should().Be("reports.interval_too_fine");
    }
}
