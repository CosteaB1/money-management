using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

public class CreatePoolCommandHandlerTests
{
    [Fact]
    public async Task Create_WritesTheCatchUpMark_ThenSeedsTheOwnerAtNavOne()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);

        CreatePoolResponse response = await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        response.MarkDelta.Should().Be(42m);
        response.SeedUnits.Should().Be(1_092m);
        response.Participants.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "Me", IsOwner = true, Units = 1_092m });

        PoolUnitEvent seed = (await harness.Db.PoolUnitEvents.ToListAsync()).Single();
        seed.Kind.Should().Be(PoolUnitEventKind.Seed);
        seed.NavPerUnit.Should().Be(1m);
        seed.PoolValuePreMoney.Should().Be(0m);

        // No cash and no movement transaction: the owner's EXISTING balance
        // simply becomes units. A 1,092 Income leg here would double the account.
        seed.Cash.Should().BeNull();
        seed.MovementTransactionId.Should().BeNull();

        List<Transaction> rows = await harness.Db.Transactions.ToListAsync();
        rows.Should().ContainSingle().Which.IsAdjustment.Should().BeTrue();
        rows.Single().CategoryId.Should().Be(SeededCategories.BalanceAdjustmentId);

        (await harness.DerivedBalanceAsync()).Should().Be(1_092m);
    }

    [Fact]
    public async Task Create_WithoutAnInceptionValue_SeedsAgainstTheDerivedBalance_AndWritesNoMark()
    {
        var harness = PoolHarness.Create(openingBalance: 1_050m);

        CreatePoolResponse response = await harness.SeedPoolAsync(poolValueAtInception: null);

        response.MarkDelta.Should().Be(0m);
        response.MarkTransactionId.Should().BeNull();
        response.SeedUnits.Should().Be(1_050m);
        (await harness.Db.Transactions.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_OnAnAccountTypeThatCannotBeAdjusted_IsRejected()
    {
        // A Cash account can never take an Adjustment, so its NAV could never be
        // re-marked and would freeze at inception.
        var harness = PoolHarness.Create(type: AccountType.Cash);

        Result<CreatePoolResponse> result = await harness.CreatePoolAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.account_type_not_eligible");
    }

    [Fact]
    public async Task Create_WithACurrencyThatIsNotTheAccounts_IsRejected()
    {
        var harness = PoolHarness.Create();

        Result<CreatePoolResponse> result = await harness.CreatePoolAsync(currency: "EUR");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.currency_mismatch");
    }

    [Fact]
    public async Task Create_OnAnAccountThatAlreadyHasAPool_IsRejected()
    {
        var harness = PoolHarness.Create();
        await harness.SeedPoolAsync();

        Result<CreatePoolResponse> second = await harness.CreatePoolAsync();

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("pools.account_already_pooled");
    }

    [Fact]
    public async Task Create_OnAMissingAccount_IsNotFound()
    {
        var harness = PoolHarness.Create();

        Result<CreatePoolResponse> result = await harness.CreatePoolAsync(accountId: Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }

    [Fact]
    public async Task Create_WithBackdatedSubscriptions_PricesEachAtItsOwnPreMoneyNav()
    {
        var harness = PoolHarness.Create(
            now: new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc),
            openingBalance: 3_092m);

        var inception = new DateOnly(2026, 6, 1);

        // The friends' money is ALREADY on the account (that is what "backfill"
        // means), so the opening balance includes it and no movement rows are
        // synthesized.
        CreatePoolResponse response = await harness.SeedPoolAsync(
            inceptionDate: inception,
            poolValueAtInception: 1_092m,
            backfills:
            [
                new BackdatedSubscription("Andrei", new DateOnly(2026, 6, 8), 1_000m, PoolValuePreMoney: 1_092m),
                new BackdatedSubscription("Bogdan", new DateOnly(2026, 6, 12), 1_000m, PoolValuePreMoney: 2_175.68m),
            ]);

        response.Participants.Should().HaveCount(3);
        response.Participants[0].IsOwner.Should().BeTrue();

        CreatedPoolParticipant andrei = response.Participants.Single(p => p.Name == "Andrei");
        CreatedPoolParticipant bogdan = response.Participants.Single(p => p.Name == "Bogdan");

        // Andrei arrived at NAV 1.0, Bogdan 4% higher — the ~USD 21/month a
        // capital-weighted split gets wrong.
        andrei.Units.Should().Be(1_000m);
        bogdan.Units.Should().Be(961.538461538462m);

        // Only the inception catch-up mark; no synthesized Income legs.
        List<Transaction> rows = await harness.Db.Transactions.ToListAsync();
        rows.Should().ContainSingle().Which.IsAdjustment.Should().BeTrue();

        List<PoolUnitEvent> events = await harness.Db.PoolUnitEvents.ToListAsync();
        events.Where(e => e.Kind == PoolUnitEventKind.Subscription)
            .Should().AllSatisfy(e => e.MovementTransactionId.Should().BeNull());
    }

    [Fact]
    public async Task Create_WithABackfillThatAsksForItsMovementRow_WritesTheTransferLeg()
    {
        var harness = PoolHarness.Create(
            now: new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc),
            openingBalance: 1_092m);

        var inception = new DateOnly(2026, 6, 1);

        CreatePoolResponse response = await harness.SeedPoolAsync(
            inceptionDate: inception,
            poolValueAtInception: 1_092m,
            backfills:
            [
                new BackdatedSubscription(
                    "Andrei",
                    new DateOnly(2026, 6, 8),
                    1_000m,
                    PoolValuePreMoney: 1_092m,
                    WriteMovementTransaction: true),
            ]);

        response.Participants.Should().HaveCount(2);

        Transaction leg = (await harness.Db.Transactions.ToListAsync())
            .Single(t => t.CategoryId == SeededCategories.PoolId);

        leg.Direction.Should().Be(TransactionDirection.Income);
        leg.IsTransfer.Should().BeTrue();
        leg.TransactionDate.Should().Be(new DateOnly(2026, 6, 8));
        leg.Description.Should().Be("Pool subscription from Andrei");

        (await harness.DerivedBalanceAsync()).Should().Be(2_092m);
    }

    [Fact]
    public async Task Create_BackdatingTheInception_MarksAsOfThatDate_NotDateBlind()
    {
        // A transaction dated AFTER inception must stay outside the catch-up
        // delta. This is exactly why creation may back-date and AdjustBalance
        // may not — AdjustBalance's delta is computed against a date-blind sum.
        var harness = PoolHarness.Create(
            now: new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc),
            openingBalance: 1_000m);

        Transaction later = Transaction.Create(
            harness.Account.Id,
            new DateOnly(2026, 6, 15),
            TransactionDirection.Income,
            new Domain.Common.Money(200m, PoolHarness.Currency),
            "Later deposit",
            TransactionSource.Manual).Value;

        harness.Db.Transactions.Add(later);

        CreatePoolResponse response = await harness.SeedPoolAsync(
            inceptionDate: new DateOnly(2026, 6, 1),
            poolValueAtInception: 1_050m);

        // 1,050 against the 1,000 the account held ON 1 June — the 200 that
        // arrived on the 15th is untouched.
        response.MarkDelta.Should().Be(50m);
        (await harness.DerivedBalanceAsync()).Should().Be(1_250m);
    }

    [Fact]
    public async Task Create_WithAZeroValuedAccountAndNoInceptionValue_IsRejected()
    {
        // A seed must mint units, and units must be positive.
        var harness = PoolHarness.Create(openingBalance: 0m);

        Result<CreatePoolResponse> result = await harness.CreatePoolAsync(poolValueAtInception: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.pool_value_must_be_positive");
    }
}
