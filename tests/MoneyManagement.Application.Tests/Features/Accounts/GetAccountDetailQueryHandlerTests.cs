using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Accounts.GetAccountDetail;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Accounts;

public class GetAccountDetailQueryHandlerTests
{
    private static readonly DateOnly OpeningDate = new(2026, 1, 15);
    private static readonly DateOnly TxDate = new(2026, 3, 10);
    private static readonly DateTime ClockNow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Account NewAccount(string name, string currency, decimal opening, AccountType type = AccountType.Cash)
    {
        Result<Account> result = Account.Create(
            name,
            type,
            new Money(opening, currency),
            OpeningDate,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction NewTransaction(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        string currency = "MDL",
        DateOnly? date = null,
        bool isTransfer = false,
        bool isAdjustment = false,
        Guid? counterAccountId = null)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date ?? TxDate,
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

    private static IFxConverter TableConverter(Dictionary<string, decimal> ratesToMdl)
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

                if (string.Equals(from, to, StringComparison.Ordinal))
                {
                    return Task.FromResult<decimal?>(amount);
                }

                if (to == "MDL" && ratesToMdl.TryGetValue(from, out decimal rate))
                {
                    return Task.FromResult<decimal?>(amount * rate);
                }

                return Task.FromResult<decimal?>(null);
            });
        return fx;
    }

    private static IDateTimeProvider Clock(DateTime? utcNow = null)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow ?? ClockNow);
        return clock;
    }

    [Fact]
    public async Task Returns_account_not_found_for_unknown_id()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("account.not_found");
    }

    [Fact]
    public async Task Returns_detail_for_archived_account()
    {
        Account account = NewAccount("Closed wallet", "MDL", 500m);
        account.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsArchived.Should().BeTrue();
        result.Value.Id.Should().Be(account.Id);
    }

    [Fact]
    public async Task Computes_initial_capital_from_anchor()
    {
        Account account = NewAccount("Brokerage", "MDL", 1_500m);
        Transaction income = NewTransaction(account.Id, TransactionDirection.Income, 300m);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [income]);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        // InitialCapital is the anchor — independent of any subsequent activity.
        result.Value.InitialCapital.Should().Be(1_500m);
    }

    [Fact]
    public async Task Computes_live_balance_with_all_non_deleted_rows()
    {
        // Opening 1000 MDL.
        // + 200 income, - 50 expense, + 400 transfer-in, - 100 transfer-out,
        // + 75 positive adjustment, - 25 negative adjustment.
        // Live balance = 1000 + 200 - 50 + 400 - 100 + 75 - 25 = 1500.
        Account account = NewAccount("Cash MDL", "MDL", 1_000m);
        var counterAccountId = Guid.NewGuid();

        Transaction[] transactions =
        [
            NewTransaction(account.Id, TransactionDirection.Income, 200m),
            NewTransaction(account.Id, TransactionDirection.Expense, 50m),
            NewTransaction(account.Id, TransactionDirection.Income, 400m,
                isTransfer: true, counterAccountId: counterAccountId),
            NewTransaction(account.Id, TransactionDirection.Expense, 100m,
                isTransfer: true, counterAccountId: counterAccountId),
            NewTransaction(account.Id, TransactionDirection.Income, 75m, isAdjustment: true),
            NewTransaction(account.Id, TransactionDirection.Expense, 25m, isAdjustment: true),
        ];

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: transactions);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.Balance.Should().Be(1_500m);
        result.Value.BalanceMdl.Should().Be(1_500m); // MDL identity
    }

    [Fact]
    public async Task Buckets_contributions_correctly()
    {
        // Only IsTransfer && Income rows should count as contributions.
        Account account = NewAccount("Brokerage", "MDL", 0m);
        var counterAccountId = Guid.NewGuid();

        Transaction[] transactions =
        [
            // Two inbound transfer legs — both contributions.
            NewTransaction(account.Id, TransactionDirection.Income, 1_000m,
                isTransfer: true, counterAccountId: counterAccountId),
            NewTransaction(account.Id, TransactionDirection.Income, 500m,
                isTransfer: true, counterAccountId: counterAccountId),
            // Outbound transfer — withdrawal, NOT a contribution.
            NewTransaction(account.Id, TransactionDirection.Expense, 200m,
                isTransfer: true, counterAccountId: counterAccountId),
            // Income adjustment — P&L, NOT a contribution.
            NewTransaction(account.Id, TransactionDirection.Income, 99m, isAdjustment: true),
            // Plain income — real activity, NOT a contribution.
            NewTransaction(account.Id, TransactionDirection.Income, 33m),
        ];

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: transactions);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.AllTime.ContributionsMdl.Should().Be(1_500m);
        result.Value.AllTime.ContributionCount.Should().Be(2);
    }

    [Fact]
    public async Task Buckets_withdrawals_correctly()
    {
        // Only IsTransfer && Expense rows count as withdrawals.
        Account account = NewAccount("Brokerage", "MDL", 5_000m);
        var counterAccountId = Guid.NewGuid();

        Transaction[] transactions =
        [
            NewTransaction(account.Id, TransactionDirection.Expense, 800m,
                isTransfer: true, counterAccountId: counterAccountId),
            NewTransaction(account.Id, TransactionDirection.Expense, 300m,
                isTransfer: true, counterAccountId: counterAccountId),
            // Inbound transfer — contribution, NOT a withdrawal.
            NewTransaction(account.Id, TransactionDirection.Income, 100m,
                isTransfer: true, counterAccountId: counterAccountId),
            // Expense adjustment — P&L, NOT a withdrawal.
            NewTransaction(account.Id, TransactionDirection.Expense, 50m, isAdjustment: true),
            // Plain expense — real activity, NOT a withdrawal.
            NewTransaction(account.Id, TransactionDirection.Expense, 20m),
        ];

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: transactions);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.AllTime.WithdrawalsMdl.Should().Be(1_100m);
        result.Value.AllTime.WithdrawalCount.Should().Be(2);
    }

    [Fact]
    public async Task Buckets_net_pnl_with_signed_direction()
    {
        // Income adjustment adds, Expense adjustment subtracts. Pure transfers
        // and real activity must NOT leak into NetPnL.
        Account account = NewAccount("Brokerage", "MDL", 2_000m);
        var counterAccountId = Guid.NewGuid();

        Transaction[] transactions =
        [
            // +400 P&L
            NewTransaction(account.Id, TransactionDirection.Income, 400m, isAdjustment: true),
            // -150 P&L
            NewTransaction(account.Id, TransactionDirection.Expense, 150m, isAdjustment: true),
            // +60 P&L
            NewTransaction(account.Id, TransactionDirection.Income, 60m, isAdjustment: true),
            // Transfers — NOT P&L.
            NewTransaction(account.Id, TransactionDirection.Income, 10_000m,
                isTransfer: true, counterAccountId: counterAccountId),
            NewTransaction(account.Id, TransactionDirection.Expense, 9_999m,
                isTransfer: true, counterAccountId: counterAccountId),
            // Real activity — NOT P&L.
            NewTransaction(account.Id, TransactionDirection.Income, 77m),
        ];

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: transactions);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.AllTime.NetPnLMdl.Should().Be(310m); // 400 - 150 + 60
        result.Value.AllTime.AdjustmentCount.Should().Be(3);
    }

    [Fact]
    public async Task Soft_deleted_rows_excluded()
    {
        Account account = NewAccount("Brokerage", "MDL", 1_000m);
        var counterAccountId = Guid.NewGuid();

        Transaction kept = NewTransaction(account.Id, TransactionDirection.Income, 200m,
            isTransfer: true, counterAccountId: counterAccountId);
        Transaction deletedContrib = NewTransaction(account.Id, TransactionDirection.Income, 999m,
            isTransfer: true, counterAccountId: counterAccountId);
        deletedContrib.MarkDeleted();
        Transaction deletedAdj = NewTransaction(account.Id, TransactionDirection.Income, 500m,
            isAdjustment: true);
        deletedAdj.MarkDeleted();
        Transaction deletedReal = NewTransaction(account.Id, TransactionDirection.Income, 50m);
        deletedReal.MarkDeleted();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [kept, deletedContrib, deletedAdj, deletedReal]);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.AllTime.ContributionsMdl.Should().Be(200m);
        result.Value.AllTime.ContributionCount.Should().Be(1);
        result.Value.AllTime.NetPnLMdl.Should().Be(0m);
        result.Value.AllTime.AdjustmentCount.Should().Be(0);
        result.Value.RealActivityCount.Should().Be(0);
        // Live balance excludes the soft-deleted rows: 1000 + 200 = 1200.
        result.Value.Balance.Should().Be(1_200m);
    }

    [Fact]
    public async Task RealActivityCount_excludes_transfers_and_adjustments()
    {
        Account account = NewAccount("Cash MDL", "MDL", 0m);
        var counterAccountId = Guid.NewGuid();

        Transaction[] transactions =
        [
            // 3 real activity rows.
            NewTransaction(account.Id, TransactionDirection.Income, 100m),
            NewTransaction(account.Id, TransactionDirection.Expense, 30m),
            NewTransaction(account.Id, TransactionDirection.Expense, 20m),
            // Transfer leg — not real activity.
            NewTransaction(account.Id, TransactionDirection.Income, 1_000m,
                isTransfer: true, counterAccountId: counterAccountId),
            // Adjustment — not real activity.
            NewTransaction(account.Id, TransactionDirection.Income, 50m, isAdjustment: true),
        ];

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: transactions);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.RealActivityCount.Should().Be(3);
    }

    [Fact]
    public async Task Ytd_window_includes_jan_1_excludes_prior_year()
    {
        // Clock anchored mid-year 2026 -> YTD = [2026-01-01, 2026-05-20].
        Account account = NewAccount("Brokerage", "MDL", 0m, type: AccountType.BankCurrent);
        var counterAccountId = Guid.NewGuid();

        Transaction priorYearDec31 = NewTransaction(account.Id, TransactionDirection.Income, 1_000m,
            date: new DateOnly(2025, 12, 31),
            isTransfer: true, counterAccountId: counterAccountId);
        Transaction ytdJan1 = NewTransaction(account.Id, TransactionDirection.Income, 200m,
            date: new DateOnly(2026, 1, 1),
            isTransfer: true, counterAccountId: counterAccountId);
        Transaction ytdMid = NewTransaction(account.Id, TransactionDirection.Income, 50m,
            date: new DateOnly(2026, 4, 15),
            isTransfer: true, counterAccountId: counterAccountId);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [priorYearDec31, ytdJan1, ytdMid]);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        // All-time gets every contribution (1000 + 200 + 50 = 1250).
        result.Value.AllTime.ContributionsMdl.Should().Be(1_250m);
        result.Value.AllTime.ContributionCount.Should().Be(3);

        // YTD includes Jan 1 (boundary) and April 15, excludes Dec 31 prior year.
        result.Value.YearToDate.ContributionsMdl.Should().Be(250m);
        result.Value.YearToDate.ContributionCount.Should().Be(2);
    }

    [Fact]
    public async Task Missing_fx_rate_flags_bucket_and_omits_row()
    {
        // USD rate present, CHF rate missing. The USD row tallies and the CHF
        // row trips MissingFxRate for THAT bucket only.
        Account account = NewAccount("Multi-fx wallet", "USD", 0m);
        var counterAccountId = Guid.NewGuid();

        // Inbound transfers in two different currencies.
        Transaction usdContrib = NewTransaction(account.Id, TransactionDirection.Income, 100m,
            currency: "USD",
            isTransfer: true, counterAccountId: counterAccountId);
        Transaction chfContrib = NewTransaction(account.Id, TransactionDirection.Income, 50m,
            currency: "CHF",
            isTransfer: true, counterAccountId: counterAccountId);

        // An adjustment in a convertible currency — its bucket should NOT be flagged.
        Transaction usdAdj = NewTransaction(account.Id, TransactionDirection.Income, 10m,
            currency: "USD", isAdjustment: true);

        IFxConverter fx = TableConverter(new Dictionary<string, decimal>
        {
            ["USD"] = 17m,
        });

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [usdContrib, chfContrib, usdAdj]);

        var handler = new GetAccountDetailQueryHandler(db, fx, Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        // Contributions: USD row converts (100 * 17 = 1700), CHF row is omitted but flagged.
        result.Value.AllTime.ContributionsMdl.Should().Be(1_700m);
        result.Value.AllTime.ContributionCount.Should().Be(2);
        result.Value.AllTime.MissingFxRate.Should().BeTrue();

        // P&L: USD adjustment converts (10 * 17 = 170), no missing flag.
        result.Value.AllTime.NetPnLMdl.Should().Be(170m);
        result.Value.AllTime.AdjustmentCount.Should().Be(1);
    }

    [Fact]
    public async Task First_and_last_activity_dates()
    {
        Account account = NewAccount("Cash MDL", "MDL", 0m);

        Transaction[] transactions =
        [
            NewTransaction(account.Id, TransactionDirection.Income, 10m, date: new DateOnly(2026, 2, 1)),
            NewTransaction(account.Id, TransactionDirection.Income, 10m, date: new DateOnly(2026, 4, 30)),
            NewTransaction(account.Id, TransactionDirection.Income, 10m, date: new DateOnly(2026, 3, 15)),
        ];

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: transactions);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.FirstActivityDate.Should().Be(new DateOnly(2026, 2, 1));
        result.Value.LastActivityDate.Should().Be(new DateOnly(2026, 4, 30));
    }

    [Fact]
    public async Task First_and_last_activity_null_when_no_transactions()
    {
        Account account = NewAccount("Cash MDL", "MDL", 500m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.Value.FirstActivityDate.Should().BeNull();
        result.Value.LastActivityDate.Should().BeNull();
        result.Value.RealActivityCount.Should().Be(0);
        result.Value.AllTime.ContributionCount.Should().Be(0);
        result.Value.AllTime.WithdrawalCount.Should().Be(0);
        result.Value.AllTime.AdjustmentCount.Should().Be(0);
    }

    [Fact]
    public async Task Withdrawal_and_adjustment_with_unconvertible_currency_flag_missing_fx()
    {
        // A USD account whose withdrawal-transfer and adjustment rows cannot be
        // converted to MDL (identity converter returns null for USD->MDL). This
        // drives the withdrawal-missing and adjustment-missing FX branches.
        Account account = NewAccount("USD broker", "USD", 1_000m, type: AccountType.Brokerage);
        var counter = Guid.CreateVersion7();

        Transaction withdrawal = NewTransaction(
            account.Id, TransactionDirection.Expense, 100m, currency: "USD",
            isTransfer: true, counterAccountId: counter);
        Transaction adjustment = NewTransaction(
            account.Id, TransactionDirection.Income, 50m, currency: "USD", isAdjustment: true);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [withdrawal, adjustment]);

        var handler = new GetAccountDetailQueryHandler(db, IdentityConverter(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(
            new GetAccountDetailQuery(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AllTime.WithdrawalCount.Should().Be(1);
        result.Value.AllTime.AdjustmentCount.Should().Be(1);
        result.Value.AllTime.MissingFxRate.Should().BeTrue();
    }
}
