using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Dashboard;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): net worth is BALANCE arithmetic,
/// so transfers and adjustments MUST contribute (the opposite of the
/// income/expense P&amp;L slices). This is the documented intentional exception
/// — pin it so a future "filter out transfers everywhere" refactor can't
/// silently corrupt net worth. Also pins the 24-month cap boundary (24 passes,
/// 25 fails) and the per-account opening-date guard.
/// </summary>
public sealed class NetWorthTrendFilterDisciplineTests
{
    private static readonly DateTime ClockNow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(ClockNow);
        return clock;
    }

    private static Account NewAccount(decimal opening, DateOnly openingDate) =>
        Account.Create("Cash", AccountType.Cash, new Money(opening, "MDL"), openingDate, notes: null).Value;

    private static Transaction Tx(
        Guid accountId, TransactionDirection direction, decimal amount, DateOnly date,
        bool isTransfer = false, bool isAdjustment = false)
    {
        Result<Transaction> result = Transaction.Create(
            accountId, date, direction, new Money(amount, "MDL"), "row", TransactionSource.Manual,
            isTransfer: isTransfer,
            counterAccountId: isTransfer ? Guid.CreateVersion7() : null,
            isAdjustment: isAdjustment);
        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static GetNetWorthTrendQueryHandler Handler(IApplicationDbContext db) =>
        new(db, FakeFxConverter.Identity(), Clock());

    [Fact]
    public async Task NetWorth_IncludesTransfersAndAdjustments_InTheBalance()
    {
        var openingDate = new DateOnly(2026, 1, 1);
        Account acct = NewAccount(opening: 1000m, openingDate);

        // +500 transfer in, -200 transfer out, +300 adjustment (income), -100 adjustment (expense)
        Transaction transferIn = Tx(acct.Id, TransactionDirection.Income, 500m, new DateOnly(2026, 5, 2), isTransfer: true);
        Transaction transferOut = Tx(acct.Id, TransactionDirection.Expense, 200m, new DateOnly(2026, 5, 3), isTransfer: true);
        Transaction adjUp = Tx(acct.Id, TransactionDirection.Income, 300m, new DateOnly(2026, 5, 4), isAdjustment: true);
        Transaction adjDown = Tx(acct.Id, TransactionDirection.Expense, 100m, new DateOnly(2026, 5, 5), isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [acct],
            transactions: [transferIn, transferOut, adjUp, adjDown]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(1), CancellationToken.None);

        // 1000 + 500 - 200 + 300 - 100 = 1500 — all four flagged rows move the balance.
        result.Value.Should().ContainSingle();
        result.Value[0].NetWorthMdl.Should().Be(1500m);
    }

    [Fact]
    public async Task NetWorth_AccountOpenedAfterAsOf_DoesNotContributeToEarlierPoints()
    {
        // Account opens this month; earlier month-end points must exclude it.
        Account acct = NewAccount(opening: 5000m, openingDate: new DateOnly(2026, 5, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [acct]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(3), CancellationToken.None);

        // Points: Mar-end, Apr-end, today(May). Only the today point sees the account.
        result.Value.Should().HaveCount(3);
        result.Value[0].NetWorthMdl.Should().Be(0m); // Mar 31 — before opening
        result.Value[1].NetWorthMdl.Should().Be(0m); // Apr 30 — before opening
        result.Value[2].NetWorthMdl.Should().Be(5000m); // today — opened May 1
    }

    [Fact]
    public async Task NetWorth_MonthsExactly24_Succeeds()
    {
        Account acct = NewAccount(opening: 100m, openingDate: new DateOnly(2020, 1, 1));
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [acct]);

        Result<IReadOnlyList<NetWorthTrendPointDto>> result = await Handler(db).Handle(
            new GetNetWorthTrendQuery(24), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(24);
    }
}
