using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.GetPoolDetail;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// The <c>/pools/{id}</c> drill-in: the pool folded today, the roster with
/// per-participant economics, the ledger newest-first, the two warnings the
/// frontend has to raise, and the reconciliation tripwire.
/// </summary>
public sealed class GetPoolDetailQueryHandlerTests
{
    private const decimal UsdMdl = 17.2682m;

    private static readonly DateTime Start = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

    private static IFxConverter Mdl() =>
        FakeFxConverter.WithTable(new Dictionary<string, decimal> { [PoolHarness.Currency] = UsdMdl });

    private static Task<Result<PoolDetailDto>> DetailAsync(PoolHarness harness, Guid? id = null) =>
        new GetPoolDetailQueryHandler(harness.Db, Mdl(), harness.Clock)
            .Handle(new GetPoolDetailQuery(id ?? harness.PoolId), CancellationToken.None);

    private static void Succeeded<T>(Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

    [Fact]
    public async Task Handle_UnknownId_ReturnsPoolsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var clock = new MutableClock(Start);

        Result<PoolDetailDto> result = await new GetPoolDetailQueryHandler(db, Mdl(), clock)
            .Handle(new GetPoolDetailQuery(Guid.CreateVersion7()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.not_found");
    }

    [Fact]
    public async Task Handle_ArchivedPool_IsStillDrillable()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        // Archiving requires zero outside units, so what is left is a finished
        // story the user should still be able to read — the loans/goals
        // precedent, and the reason the handler loads with IgnoreQueryFilters.
        Result<PoolDetailDto> result = await DetailAsync(harness);

        Succeeded(result);
        result.Value.IsArchived.Should().BeTrue();
        result.Value.TotalUnits.Should().Be(1_092m);
        result.Value.Events.Should().ContainSingle().Which.Kind.Should().Be(PoolUnitEventKind.Seed);
    }

    [Fact]
    public async Task Handle_MarkAgeDays_CountsFromTheLastConfirmedValue()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_092m, 1_000m));

        // 06-10: a real mark row (+308), and a PARTIAL payout so there is still
        // profit left to distribute at the next close.
        harness.Advance(2);
        Result<CloseDistributionResponse> first =
            await harness.CloseAsync(2_400m, [new DistributionPayout(andrei, 50m)]);
        Succeeded(first);
        first.Value.MarkTransactionId.Should().NotBeNull();

        harness.Advance(2);
        Succeeded(await harness.SettleAsync(first.Value.Lines.Single().EventId));

        // 06-14: a close that types EXACTLY the value the app already derives.
        // PoolMark skips a zero delta, so NO adjustment row is written at all —
        // which is the normal case for a pool priced on schedule.
        harness.Advance(2);
        Result<CloseDistributionResponse> second = await harness.CloseAsync(2_350m);
        Succeeded(second);
        second.Value.MarkDelta.Should().Be(0m);
        second.Value.MarkTransactionId.Should().BeNull();

        List<Transaction> adjustments = await harness.Db.Transactions
            .Where(t => t.AccountId == harness.Account.Id && t.IsAdjustment)
            .ToListAsync();

        adjustments.Max(t => t.TransactionDate)
            .Should().Be(new DateOnly(2026, 6, 10), "the last adjustment ROW really is four days old");

        Result<PoolDetailDto> result = await DetailAsync(harness);
        Succeeded(result);

        // Staleness is max(latest adjustment row, latest non-Seed unit event) —
        // every priced event made the user type the pre-money total, so its date
        // confirms the value just as hard as a row does. Counting rows alone
        // would march this pool past 30/60/90 days of invented staleness on the
        // one tripwire the design says the user has to trust.
        result.Value.LastMarkDate.Should().Be(new DateOnly(2026, 6, 14));
        result.Value.MarkAgeDays.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EventsAreNewestFirst_WithTheIdBreakingSameDayTies()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        Succeeded(await harness.SubscribeAsync(andrei, 1_092m, 1_000m));

        Guid bogdan = await harness.AddParticipantAsync("Bogdan");
        Succeeded(await harness.SubscribeAsync(bogdan, 2_092m, 1_000m));

        Result<PoolDetailDto> result = await DetailAsync(harness);
        Succeeded(result);

        IReadOnlyList<PoolUnitEventDto> events = result.Value.Events;
        events.Should().HaveCount(3);

        // Newest day first; the seed is the tail.
        events.Select(e => e.OccurredOn).Should().Equal(
            new DateOnly(2026, 6, 8),
            new DateOnly(2026, 6, 8),
            new DateOnly(2026, 6, 6));

        events[^1].Kind.Should().Be(PoolUnitEventKind.Seed);

        // Same day: the id (UUIDv7) breaks the tie DESCENDING, so the most
        // recently recorded event leads — GetLoanDetail's convention, with the
        // time-ordered id standing in for CreatedAt.
        events[0].Id.CompareTo(events[1].Id).Should().BePositive();
    }

    [Fact]
    public async Task Handle_RedemptionToAnotherAccount_LabelsTheMovementWithTheDestinationName()
    {
        Account bybit = Account.Create(
            "Bybit",
            AccountType.CryptoExchange,
            new Money(0m, PoolHarness.Currency),
            new DateOnly(2026, 1, 1),
            notes: null).Value;

        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m, extraAccounts: bybit);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        harness.Advance(2);
        Result<RecordRedemptionResponse> withdrawal =
            await harness.RedeemAsync(harness.OwnerId, 1_092m, 400m, destinationAccountId: bybit.Id);

        Succeeded(withdrawal);
        withdrawal.Value.CounterTransactionId.Should().NotBeNull();

        Result<PoolDetailDto> result = await DetailAsync(harness);
        Succeeded(result);

        PoolUnitEventDto redemption = result.Value.Events
            .Single(e => e.Kind == PoolUnitEventKind.Redemption);

        // NOTE — this is deliberately NOT the destination. RecordRedemption links
        // the unit event to the POOL-SIDE leg ("that is the row whose deletion
        // would re-price units"), so the movement is labelled with the account the
        // units left, not the account the cash landed in. The Bybit side is a
        // reciprocal transfer leg with no unit event of its own.
        redemption.MovementTransactionId.Should().Be(withdrawal.Value.MovementTransactionId);
        redemption.MovementAccountId.Should().Be(harness.Account.Id);
        redemption.MovementAccountName.Should().Be("Binance");

        Transaction counterLeg = await harness.Db.Transactions
            .SingleAsync(t => t.Id == withdrawal.Value.CounterTransactionId!.Value);

        counterLeg.AccountId.Should().Be(bybit.Id);
        counterLeg.CounterAccountId.Should().Be(harness.Account.Id);
        counterLeg.Direction.Should().Be(TransactionDirection.Income);
        counterLeg.IsTransfer.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WorkedExampleAfterTheClose_MatchesTheHandComputedTable()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughCloseAsync();

        Result<PoolDetailDto> result = await DetailAsync(example.Harness);
        Succeeded(result);

        PoolDetailDto detail = result.Value;

        detail.AccountBalance.Should().Be(PoolWorkedExample.ClosePoolValue);
        detail.UnpaidDistributionCash.Should().Be(PoolWorkedExample.TotalPayout);
        detail.UnpaidDistributionCount.Should().Be(2);
        detail.PoolValue.Should().Be(PoolWorkedExample.PoolValueAfterClose);
        detail.TotalUnits.Should().Be(PoolWorkedExample.TotalUnitsAfterClose);
        detail.NavPerUnit.Should().Be(PoolWorkedExample.NavAtClose);
        detail.OutsideCapital.Should().Be(2_000m);
        detail.MissingFxRate.Should().BeFalse();
        detail.PoolValueMdl.Should().Be(PoolWorkedExample.PoolValueAfterClose * UsdMdl);

        // Owner first, then by JoinedOn.
        detail.Participants.Select(p => p.Name).Should().Equal("Me", "Andrei", "Bogdan");

        PoolParticipantDto owner = detail.Participants[0];
        PoolParticipantDto andrei = detail.Participants[1];
        PoolParticipantDto bogdan = detail.Participants[2];

        owner.IsOwner.Should().BeTrue();
        owner.Units.Should().Be(PoolWorkedExample.OwnerUnits);

        // §6's post-close table prints 778.13 for the owner. It also prints 778.12
        // for the SAME units at the SAME NAV in the pre-close table one paragraph
        // earlier — a close redeems nobody else's units at the owner's expense, so
        // the two cannot differ. 707.384615384615 × 1.100001729306 = 778.1243…,
        // which is 778.12. The document's second figure is the arithmetic slip.
        owner.Stake.Should().Be(778.12m);
        owner.StakeMdl.Should().Be(778.12m * UsdMdl);

        // A seed carries no cash, and 400 went out to Bybit, so the owner's
        // capital base is legitimately NEGATIVE. It is floored at zero only for
        // the distributable, never for the reported base.
        owner.CapitalBase.Should().Be(-400m);
        owner.Distributable.Should().Be(778.12m, "the whole stake is profit once the base is recovered");
        owner.UnpaidDistributionCash.Should().Be(0m, "the default close excludes the owner on purpose");

        andrei.Units.Should().Be(PoolWorkedExample.AndreiUnitsAfterClose);
        andrei.Stake.Should().Be(1_000m);
        andrei.CapitalBase.Should().Be(1_000m);
        andrei.Distributable.Should().Be(0m, "the payout swept the stake back to exactly the capital base");
        andrei.UnpaidDistributionCash.Should().Be(PoolWorkedExample.AndreiPayout);
        andrei.UnpaidDistributionCount.Should().Be(1);

        bogdan.Units.Should().Be(PoolWorkedExample.BogdanUnitsAfterClose);
        bogdan.Stake.Should().Be(1_000m);
        bogdan.CapitalBase.Should().Be(1_000m);
        bogdan.Distributable.Should().Be(0m);
        bogdan.UnpaidDistributionCash.Should().Be(PoolWorkedExample.BogdanPayout);
        bogdan.UnpaidDistributionCount.Should().Be(1);

        // Both friends are back at their 1,000 base holding ≈909 units, despite
        // entering at different NAVs. Nothing says "reset their base" anywhere.
        andrei.Units.Should().BeApproximately(909.09m, 0.01m);
        bogdan.Units.Should().BeApproximately(909.09m, 0.01m);

        owner.OwnershipPercent.Should().Be(28.008923m);
        andrei.OwnershipPercent.Should().Be(35.995498m);
        bogdan.OwnershipPercent.Should().Be(35.995579m);

        detail.OwnerParticipantId.Should().Be(example.OwnerId);
        detail.OwnerFraction.Should().Be(
            PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterClose);

        // PoolDto.OwnerFraction's contract, while a payout is closed but unpaid:
        // this page's fraction is applied to PoolValue (owed cash netted out) and
        // the dashboard's to the RAW AccountBalance (units still outstanding), so
        // the two FRACTIONS differ — and the two VALUES agree to the cent.
        decimal dashboardFraction =
            PoolWorkedExample.OwnerUnits / PoolWorkedExample.TotalUnitsAfterWithdrawal;

        detail.OwnerFraction.Should().NotBe(dashboardFraction);
        Math.Round(detail.PoolValue * detail.OwnerFraction, 2)
            .Should().Be(Math.Round(detail.AccountBalance * dashboardFraction, 2));

        detail.Events.Where(e => e.IsUnpaid).Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ZeroUnitsPool_ReportsOwnerFractionOneAndParticipantPercentZero()
    {
        var harness = PoolHarness.Create(now: Start, openingBalance: 1_050m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_092m);

        // Driven through the REAL handler. Redeeming the last of a pool is by
        // definition redeeming the whole pool value, so it trips
        // `pools.cash_looks_like_a_balance` — `IsFullWindDown` is the deliberate,
        // verified acknowledgement that reopens exactly that case. (This test
        // used to build its event at the domain boundary because no API path
        // existed; now one does, so the read side is exercised against state a
        // user can actually produce.)
        Result<RecordRedemptionResponse> windUp = await harness.RedeemAsync(
            harness.OwnerId,
            poolValueNow: 1_092m,
            cash: 1_092m,
            isFullWindDown: true);

        windUp.IsSuccess.Should().BeTrue(windUp.IsFailure ? windUp.Error.Code : null);
        windUp.Value.Units.Should().Be(1_092m);

        Result<PoolDetailDto> result = await DetailAsync(harness);
        Succeeded(result);

        PoolDetailDto detail = result.Value;

        detail.TotalUnits.Should().Be(0m);
        detail.NavPerUnit.Should().BeNull("no units outstanding means no price, never a substituted 1.0");

        // THE TWO CONVENTIONS DELIBERATELY DISAGREE, and this test exists so a
        // later "consistency" refactor goes red rather than silently breaking one
        // caller. They answer different questions:
        //
        //   OwnerFraction  = 1.0 — "nobody else's money is in this account", the
        //                    figure net worth multiplies the balance by. Zero here
        //                    would erase the account from the dashboard.
        //   OwnershipPercent = 0 — "what share of nothing does this participant
        //                    hold?" A share of nothing is nothing.
        detail.OwnerFraction.Should().Be(1m);
        detail.Participants.Should().ContainSingle().Which.OwnershipPercent.Should().Be(0m);

        detail.Participants[0].Units.Should().Be(0m);
        detail.Participants[0].Stake.Should().BeNull();
        detail.Participants[0].Distributable.Should().BeNull();
        detail.OutsideCapital.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_HandlerBuiltPool_ReportsACleanReconciliation()
    {
        PoolWorkedExample example = await PoolWorkedExample.ThroughSettlementAsync();

        Result<PoolDetailDto> result = await DetailAsync(example.Harness);
        Succeeded(result);

        PoolReconciliationDto reconciliation = result.Value.Reconciliation;

        reconciliation.IsClean.Should().BeTrue();
        reconciliation.UnmatchedTransactions.Should().BeEmpty();
        reconciliation.ValueDrifts.Should().BeEmpty();
        reconciliation.UnbackedCashClaims.Should().BeEmpty();
        reconciliation.UnitsBalance.Should().BeTrue();
        reconciliation.UnitsDrift.Should().Be(0m);

        // …and the whole month settled: nothing is owed any more.
        result.Value.UnpaidDistributionCash.Should().Be(0m);
        result.Value.AccountBalance.Should().Be(PoolWorkedExample.PoolValueAfterClose);
        result.Value.Events.Should().OnlyContain(e => !e.IsUnpaid);
    }
}
