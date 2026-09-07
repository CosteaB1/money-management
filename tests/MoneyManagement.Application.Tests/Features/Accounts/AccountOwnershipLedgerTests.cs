using FluentAssertions;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Tests.TestSupport;

namespace MoneyManagement.Application.Tests.Features.Accounts;

/// <summary>
/// The caller-side lookup the net-worth handlers use. Its whole job is to make
/// "no ownership data" cost nothing and read as wholly owned.
/// </summary>
public sealed class AccountOwnershipLedgerTests
{
    private static readonly DateOnly AsOf = new(2026, 5, 1);

    [Fact]
    public async Task OwnedFractionAsOf_NoSourcesAtAll_IsWhollyOwned()
    {
        AccountOwnershipLedger ledger = await AccountOwnershipLedger.LoadAsync([], CancellationToken.None);

        ledger.OwnedFractionAsOf(Guid.CreateVersion7(), AsOf).Should().Be(1m);
    }

    [Fact]
    public async Task OwnedFractionAsOf_UnknownAccountId_IsWhollyOwned()
    {
        var known = Guid.CreateVersion7();

        AccountOwnershipLedger ledger = await AccountOwnershipLedger.LoadAsync(
            [FakeAccountOwnershipSource.Flat(known, 0.25m)],
            CancellationToken.None);

        ledger.OwnedFractionAsOf(Guid.CreateVersion7(), AsOf).Should().Be(
            1m,
            "an account nobody reported on has no outside capital in it");
        ledger.OwnedFractionAsOf(known, AsOf).Should().Be(0.25m);
    }

    [Fact]
    public async Task LoadAsync_MergesEverySource()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        AccountOwnershipLedger ledger = await AccountOwnershipLedger.LoadAsync(
            [
                FakeAccountOwnershipSource.Flat(a, 0.5m),
                FakeAccountOwnershipSource.Flat(b, 0.75m),
            ],
            CancellationToken.None);

        ledger.OwnedFractionAsOf(a, AsOf).Should().Be(0.5m);
        ledger.OwnedFractionAsOf(b, AsOf).Should().Be(0.75m);
    }

    [Fact]
    public async Task LoadAsync_OneSourceWithSeveralAccounts_IndexesThemAll()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        AccountOwnershipLedger ledger = await AccountOwnershipLedger.LoadAsync(
            [
                FakeAccountOwnershipSource.With(
                    new AccountOwnership(a, [new OwnedFractionPoint(new DateOnly(2026, 1, 1), 0.5m)]),
                    new AccountOwnership(b, [new OwnedFractionPoint(new DateOnly(2026, 1, 1), 0.1m)])),
            ],
            CancellationToken.None);

        ledger.OwnedFractionAsOf(a, AsOf).Should().Be(0.5m);
        ledger.OwnedFractionAsOf(b, AsOf).Should().Be(0.1m);
    }

    [Fact]
    public async Task LoadAsync_TwoSourcesClaimingTheSameAccount_FirstOneWins()
    {
        // Documented behaviour, not an aspiration: composing two independently
        // dated step functions is arithmetic nobody has specified. First-wins at
        // least makes the outcome deterministic rather than silently
        // registration-order-dependent in a way callers cannot see.
        var shared = Guid.CreateVersion7();

        AccountOwnershipLedger ledger = await AccountOwnershipLedger.LoadAsync(
            [
                FakeAccountOwnershipSource.Flat(shared, 0.5m),
                FakeAccountOwnershipSource.Flat(shared, 0.9m),
            ],
            CancellationToken.None);

        ledger.OwnedFractionAsOf(shared, AsOf).Should().Be(0.5m);
    }

    [Fact]
    public async Task OwnedFractionAsOf_SlicesTheCurveAtTheGivenDate()
    {
        var account = Guid.CreateVersion7();

        AccountOwnershipLedger ledger = await AccountOwnershipLedger.LoadAsync(
            [
                FakeAccountOwnershipSource.With(new AccountOwnership(
                    account,
                    [
                        new OwnedFractionPoint(new DateOnly(2026, 3, 31), 0.5m),
                        new OwnedFractionPoint(new DateOnly(2026, 4, 30), 0.25m),
                    ])),
            ],
            CancellationToken.None);

        ledger.OwnedFractionAsOf(account, new DateOnly(2026, 3, 30)).Should().Be(1m);
        ledger.OwnedFractionAsOf(account, new DateOnly(2026, 3, 31)).Should().Be(0.5m);
        ledger.OwnedFractionAsOf(account, new DateOnly(2026, 4, 30)).Should().Be(0.25m);
    }
}
