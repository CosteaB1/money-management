using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Accounts.GetAccounts;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Accounts;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): pins the "Manual rate always wins
/// over BnmAuto on the same (from, to, asOf) triple" guarantee for the
/// account-list <c>BalanceMdl</c> projection. <c>EfFxConverter</c> honours this
/// via an explicit numeric tie-break, and the dashboard / reports / goals all go
/// through it — but <c>GetAccountsQueryHandler.ConvertInMemory</c> re-implements
/// the lookup against an in-memory rate snapshot and must apply the SAME
/// tie-break, or the account list disagrees with every other MDL figure.
/// </summary>
public sealed class AccountsBalanceMdlManualRatePreferenceTests
{
    private static FxRate Rate(string from, string to, decimal rate, DateOnly asOf, FxRateSource source) =>
        FxRate.Create(from, to, rate, asOf, source).Value;

    [Fact]
    public async Task BalanceMdl_PrefersManualRate_OverBnmAuto_OnSameTriple()
    {
        // USD account with a 100 USD balance (no transactions → balance == anchor).
        Account usd = Account.Create(
            "Brokerage USD", AccountType.Brokerage, new Money(100m, "USD"), new DateOnly(2026, 1, 1), notes: null).Value;

        var asOf = new DateOnly(2026, 1, 1);
        // Insert the BnmAuto row FIRST so a naive OrderByDescending(AsOf) +
        // FirstOrDefault (stable sort) would surface BnmAuto — the wrong one.
        FxRate bnm = Rate("USD", "MDL", 17m, asOf, FxRateSource.BnmAuto);
        FxRate manual = Rate("USD", "MDL", 20m, asOf, FxRateSource.Manual);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [usd],
            fxRates: [bnm, manual]);

        var handler = new GetAccountsQueryHandler(db);
        Result<IReadOnlyList<AccountDto>> result = await handler.Handle(
            new GetAccountsQuery(), CancellationToken.None);

        AccountDto dto = result.Value.Single();
        // Manual rate (20) must win: 100 USD * 20 = 2000 MDL, NOT 100 * 17 = 1700.
        dto.BalanceMdl.Should().Be(2000m);
    }
}
