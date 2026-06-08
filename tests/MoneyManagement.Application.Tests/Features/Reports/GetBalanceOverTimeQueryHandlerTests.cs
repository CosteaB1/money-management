using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Reports.GetBalanceOverTime;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Reports;

public class GetBalanceOverTimeQueryHandlerTests
{
    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    private static Account NewAccount(string name, string currency, decimal opening, DateOnly? openingDate = null)
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(opening, currency),
            openingDate ?? LongAgo,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction Tx(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        DateOnly date,
        string currency = "MDL",
        bool isTransfer = false,
        bool isAdjustment = false,
        Guid? counterAccountId = null)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date,
            direction,
            new Money(amount, currency),
            "row",
            TransactionSource.Manual,
            isTransfer: isTransfer,
            counterAccountId: counterAccountId,
            isAdjustment: isAdjustment);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static IFxConverter IdentityConverter()
    {
        IFxConverter fx = Substitute.For<IFxConverter>();
        fx.ConvertAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                decimal amount = call.ArgAt<decimal>(0);
                string from = call.ArgAt<string>(1);
                string to = call.ArgAt<string>(2);
                return Task.FromResult<decimal?>(
                    string.Equals(from, to, StringComparison.Ordinal) ? amount : null);
            });
        return fx;
    }

    [Fact]
    public async Task Handle_MonthlyInterval_BalanceTrendMatchesManualCalculation()
    {
        Account a = NewAccount("Wallet", "MDL", 1_000m);

        Transaction feb = Tx(a.Id, TransactionDirection.Income, 200m, new DateOnly(2026, 2, 10));
        Transaction marExpense = Tx(a.Id, TransactionDirection.Expense, 50m, new DateOnly(2026, 3, 5));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [feb, marExpense]);

        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        // Range Feb 1..Mar 31. Monthly interval -> Feb-end + Mar-end.
        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        // Feb-end: opening 1000 + feb 200 = 1200.
        result.Value[0].AsOf.Should().Be(new DateOnly(2026, 2, 28));
        result.Value[0].Balance.Should().Be(1_200m);
        // Mar-end: + (-50) = 1150.
        result.Value[1].AsOf.Should().Be(new DateOnly(2026, 3, 31));
        result.Value[1].Balance.Should().Be(1_150m);
    }

    [Fact]
    public async Task Handle_TransfersAndAdjustments_DOMoveThePerAccountBalance()
    {
        // The canonical inverse-of-P&L pin: balance arithmetic includes ALL
        // non-deleted rows, even transfers and adjustments. They are real
        // account movements regardless of P&L classification.
        Account a = NewAccount("Wallet", "MDL", 100m);
        var otherAccount = Guid.CreateVersion7();

        Transaction transferOut = Tx(
            a.Id, TransactionDirection.Expense, 30m, new DateOnly(2026, 3, 5),
            isTransfer: true, counterAccountId: otherAccount);
        Transaction adjustment = Tx(
            a.Id, TransactionDirection.Income, 50m, new DateOnly(2026, 3, 7),
            isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [transferOut, adjustment]);

        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        // 100 - 30 + 50 = 120. If we filtered transfers/adjustments it would be 100.
        result.Value[^1].Balance.Should().Be(120m);
    }

    [Fact]
    public async Task Handle_SoftDeletedTransactions_AreExcluded()
    {
        Account a = NewAccount("Wallet", "MDL", 100m);

        Transaction kept = Tx(a.Id, TransactionDirection.Income, 30m, new DateOnly(2026, 3, 5));
        Transaction deleted = Tx(a.Id, TransactionDirection.Income, 9_999m, new DateOnly(2026, 3, 6));
        deleted.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [kept, deleted]);

        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.Value[^1].Balance.Should().Be(130m);
    }

    [Fact]
    public async Task Handle_AccountFromAnotherAccount_DoesNotContribute()
    {
        Account a = NewAccount("Wallet", "MDL", 100m);
        var otherAccount = Guid.CreateVersion7();
        Transaction other = Tx(otherAccount, TransactionDirection.Income, 999m, new DateOnly(2026, 3, 5));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [other]);

        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.Value[^1].Balance.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_DailyInterval_ProducesOnePointPerDay()
    {
        Account a = NewAccount("Wallet", "MDL", 0m);
        Transaction tx = Tx(a.Id, TransactionDirection.Income, 10m, new DateOnly(2026, 3, 2));

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [a], transactions: [tx]);

        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3), BalanceInterval.Daily),
            CancellationToken.None);

        result.Value.Should().HaveCount(3);
        result.Value[0].Balance.Should().Be(0m);
        result.Value[1].Balance.Should().Be(10m);
        result.Value[2].Balance.Should().Be(10m);
    }

    [Fact]
    public async Task Handle_DailySpanTooWide_ReturnsIntervalTooFine()
    {
        Account a = NewAccount("Wallet", "MDL", 0m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [a]);
        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        // Just over the cap.
        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(
                a.Id,
                new DateOnly(2020, 1, 1),
                new DateOnly(2024, 1, 1),
                BalanceInterval.Daily),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reports.interval_too_fine");
    }

    [Fact]
    public async Task Handle_ArchivedAccount_ReturnsNotFound()
    {
        Account a = NewAccount("Wallet", "MDL", 100m);
        a.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [a]);
        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }

    [Fact]
    public async Task Handle_UnknownAccountId_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(Guid.CreateVersion7(), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), BalanceInterval.Monthly),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }

    [Fact]
    public async Task Handle_FromAfterTo_ReturnsRangeOutOfBounds()
    {
        Account a = NewAccount("Wallet", "MDL", 100m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [a]);
        var handler = new GetBalanceOverTimeQueryHandler(db, IdentityConverter());

        Result<IReadOnlyList<BalancePointDto>> result = await handler.Handle(
            new GetBalanceOverTimeQuery(a.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1), BalanceInterval.Monthly),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reports.range_out_of_bounds");
    }
}
