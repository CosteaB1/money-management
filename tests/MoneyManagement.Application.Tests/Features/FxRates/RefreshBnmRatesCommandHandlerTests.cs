using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.FxRates.RefreshBnmRates;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.FxRates;

public class RefreshBnmRatesCommandHandlerTests
{
    private static readonly DateOnly AsOf = new(2026, 5, 22);
    private static readonly DateTime FixedNow = new(2026, 5, 22, 9, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Account AccountWithCurrency(string currency)
    {
        Result<Account> result = Account.Create(
            $"{currency} account",
            AccountType.BankCurrent,
            new Money(100m, currency),
            new DateOnly(2026, 1, 1),
            notes: null);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static IBnmRateProvider ProviderReturning(params BnmRate[] rates)
    {
        IBnmRateProvider provider = Substitute.For<IBnmRateProvider>();
        provider.GetRatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BnmRate>>(rates));
        return provider;
    }

    private static RefreshBnmRatesCommandHandler BuildHandler(
        IApplicationDbContext db,
        IBnmRateProvider provider) =>
        new(db, provider, Clock(), NullLogger<RefreshBnmRatesCommandHandler>.Instance);

    [Fact]
    public async Task Handle_FiltersFetchedRatesToHeldCurrencies()
    {
        // User holds USD + EUR; provider returns USD/EUR/RON. RON is skipped.
        Account usd = AccountWithCurrency("USD");
        Account eur = AccountWithCurrency("EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [usd, eur]);

        IBnmRateProvider provider = ProviderReturning(
            new BnmRate("USD", 17.50m, AsOf),
            new BnmRate("EUR", 19.00m, AsOf),
            new BnmRate("RON", 3.80m, AsOf));

        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Fetched.Should().Be(3);
        result.Value.Inserted.Should().Be(2);
        result.Value.Updated.Should().Be(0);
        result.Value.Skipped.Should().Be(1);

        db.FxRates.Should().HaveCount(2);
        db.FxRates.Should().OnlyContain(r => r.Source == FxRateSource.BnmAuto);
    }

    [Fact]
    public async Task Handle_WhenManualRateAlreadyExists_SkipsBnmValueEvenIfDifferent()
    {
        Account usd = AccountWithCurrency("USD");
        // Manual rate for USD->MDL on AsOf with a hand-edited value.
        FxRate manual = FxRate.Create("USD", "MDL", 18.00m, AsOf, FxRateSource.Manual).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [usd],
            fxRates: [manual]);

        IBnmRateProvider provider = ProviderReturning(new BnmRate("USD", 17.50m, AsOf));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Skipped.Should().Be(1);
        result.Value.Inserted.Should().Be(0);
        result.Value.Updated.Should().Be(0);

        // Manual rate untouched.
        db.FxRates.Should().ContainSingle();
        FxRate persisted = db.FxRates.Single();
        persisted.Source.Should().Be(FxRateSource.Manual);
        persisted.Rate.Should().Be(18.00m);
    }

    [Fact]
    public async Task Handle_WhenBnmAutoRowExistsWithSameValue_SkipsAsNoOp()
    {
        Account usd = AccountWithCurrency("USD");
        FxRate existing = FxRate.Create("USD", "MDL", 17.50m, AsOf, FxRateSource.BnmAuto).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [usd],
            fxRates: [existing]);

        IBnmRateProvider provider = ProviderReturning(new BnmRate("USD", 17.50m, AsOf));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Skipped.Should().Be(1);
        result.Value.Inserted.Should().Be(0);
        result.Value.Updated.Should().Be(0);

        db.FxRates.Should().ContainSingle();
        db.FxRates.Single().Rate.Should().Be(17.50m);
    }

    [Fact]
    public async Task Handle_WhenBnmAutoRowExistsWithDifferentValue_UpdatesInPlace()
    {
        Account usd = AccountWithCurrency("USD");
        FxRate existing = FxRate.Create("USD", "MDL", 17.50m, AsOf, FxRateSource.BnmAuto).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [usd],
            fxRates: [existing]);

        IBnmRateProvider provider = ProviderReturning(new BnmRate("USD", 17.62m, AsOf));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Updated.Should().Be(1);
        result.Value.Inserted.Should().Be(0);
        result.Value.Skipped.Should().Be(0);

        db.FxRates.Should().ContainSingle();
        FxRate persisted = db.FxRates.Single();
        persisted.Source.Should().Be(FxRateSource.BnmAuto);
        persisted.Rate.Should().Be(17.62m);
    }

    [Fact]
    public async Task Handle_WhenProviderReturnsInvalidRateForNewRow_LogsAndSkips()
    {
        // A non-positive rate makes FxRate.Create fail; the row is skipped and
        // counted, nothing is inserted (the create-failure log branch).
        Account usd = AccountWithCurrency("USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [usd]);

        IBnmRateProvider provider = ProviderReturning(new BnmRate("USD", 0m, AsOf));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Inserted.Should().Be(0);
        result.Value.Skipped.Should().Be(1);
        db.FxRates.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenProviderReturnsInvalidRateForExistingRow_LogsAndSkips()
    {
        // An existing BnmAuto row with a different value, but the new rate is
        // non-positive so UpdateRate fails; the row is skipped (update-failure
        // log branch) and the existing value is left untouched.
        Account usd = AccountWithCurrency("USD");
        FxRate existing = FxRate.Create("USD", "MDL", 17.50m, AsOf, FxRateSource.BnmAuto).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [usd],
            fxRates: [existing]);

        IBnmRateProvider provider = ProviderReturning(new BnmRate("USD", -1m, AsOf));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Updated.Should().Be(0);
        result.Value.Skipped.Should().Be(1);
        db.FxRates.Single().Rate.Should().Be(17.50m);
    }

    [Fact]
    public async Task Handle_WhenProviderReturnsEmpty_DoesNothing()
    {
        Account usd = AccountWithCurrency("USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [usd]);

        IBnmRateProvider provider = ProviderReturning();
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new RefreshBnmRatesResponse(0, 0, 0, 0));
        db.FxRates.Should().BeEmpty();
        await provider.Received(1).GetRatesAsync(AsOf, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserHoldsOnlyMdlAccounts_SkipsProviderCall()
    {
        Account mdl = AccountWithCurrency(ReportingCurrencies.Mdl);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [mdl]);

        IBnmRateProvider provider = Substitute.For<IBnmRateProvider>();
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new RefreshBnmRatesResponse(0, 0, 0, 0));

        await provider.DidNotReceive().GetRatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        db.FxRates.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNullDate_UsesTodayUtc()
    {
        Account usd = AccountWithCurrency("USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [usd]);

        IBnmRateProvider provider = ProviderReturning(
            new BnmRate("USD", 17.50m, DateOnly.FromDateTime(FixedNow)));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(Date: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await provider.Received(1).GetRatesAsync(DateOnly.FromDateTime(FixedNow), Arg.Any<CancellationToken>());
        result.Value.Inserted.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithExplicitCurrencyFilter_OverridesAccountDerivation()
    {
        // Even though no accounts exist, the filter forces a JPY fetch.
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        IBnmRateProvider provider = ProviderReturning(
            new BnmRate("USD", 17.50m, AsOf),
            new BnmRate("JPY", 0.11312m, AsOf));
        RefreshBnmRatesCommandHandler handler = BuildHandler(db, provider);

        Result<RefreshBnmRatesResponse> result = await handler.Handle(
            new RefreshBnmRatesCommand(AsOf, CurrencyFilter: ["JPY"]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Inserted.Should().Be(1);
        result.Value.Skipped.Should().Be(1);
        db.FxRates.Should().ContainSingle().Which.FromCurrency.Should().Be("JPY");
    }
}
