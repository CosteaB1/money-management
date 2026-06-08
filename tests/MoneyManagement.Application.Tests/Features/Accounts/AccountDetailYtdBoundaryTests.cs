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

/// <summary>
/// Adversarial overnight sweep (2026-06-02): pins the AccountDetail YTD window
/// boundary inclusivity. YTD is [Jan 1 of current UTC year, today] inclusive.
/// A contribution dated exactly Jan 1 must land in YTD; one dated last Dec 31
/// must NOT; one dated exactly today must.
/// </summary>
public sealed class AccountDetailYtdBoundaryTests
{
    private static readonly DateTime ClockNow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(ClockNow);
        return clock;
    }

    private static Account Brokerage()
    {
        Result<Account> result = Account.Create(
            "Brokerage", AccountType.Brokerage, new Money(0m, "MDL"), new DateOnly(2024, 1, 1), notes: null);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction Contribution(Guid accountId, decimal amount, DateOnly date)
    {
        // Inbound transfer leg (Income + IsTransfer) → contribution bucket.
        Result<Transaction> result = Transaction.Create(
            accountId, date, TransactionDirection.Income, new Money(amount, "MDL"),
            "contribution", TransactionSource.Manual,
            isTransfer: true, counterAccountId: Guid.CreateVersion7());
        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task Ytd_ContributionOnJan1_IsInYtd()
    {
        Account acct = Brokerage();
        Transaction jan1 = Contribution(acct.Id, 100m, new DateOnly(2026, 1, 1));

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [acct], transactions: [jan1]);
        var handler = new GetAccountDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(new GetAccountDetailQuery(acct.Id), CancellationToken.None);

        result.Value.AllTime.ContributionsMdl.Should().Be(100m);
        result.Value.YearToDate.ContributionsMdl.Should().Be(100m);
        result.Value.YearToDate.ContributionCount.Should().Be(1);
    }

    [Fact]
    public async Task Ytd_ContributionOnPriorDec31_IsExcludedFromYtd_ButInAllTime()
    {
        Account acct = Brokerage();
        Transaction dec31 = Contribution(acct.Id, 100m, new DateOnly(2025, 12, 31));

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [acct], transactions: [dec31]);
        var handler = new GetAccountDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(new GetAccountDetailQuery(acct.Id), CancellationToken.None);

        result.Value.AllTime.ContributionsMdl.Should().Be(100m);
        result.Value.YearToDate.ContributionsMdl.Should().Be(0m);
        result.Value.YearToDate.ContributionCount.Should().Be(0);
    }

    [Fact]
    public async Task Ytd_ContributionOnToday_IsInYtd()
    {
        Account acct = Brokerage();
        var today = DateOnly.FromDateTime(ClockNow);
        Transaction todayTx = Contribution(acct.Id, 250m, today);

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [acct], transactions: [todayTx]);
        var handler = new GetAccountDetailQueryHandler(db, FakeFxConverter.Identity(), Clock());

        Result<AccountDetailDto> result = await handler.Handle(new GetAccountDetailQuery(acct.Id), CancellationToken.None);

        result.Value.YearToDate.ContributionsMdl.Should().Be(250m);
        result.Value.YearToDate.ContributionCount.Should().Be(1);
    }
}
