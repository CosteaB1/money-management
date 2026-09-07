using FluentAssertions;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Pools;

public class PoolUnitEventTests
{
    private const string Currency = "USD";

    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly Inception = new(2026, 4, 1);
    private static readonly DateOnly EventDate = new(2026, 4, 30);
    private static readonly Guid PoolId = Guid.CreateVersion7();
    private static readonly Guid ParticipantId = Guid.CreateVersion7();

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock() => new FixedClock(FixedNow);

    /// <summary>Everything explicit — the tests below break one thing at a time.</summary>
    private static Result<PoolUnitEvent> Create(
        PoolUnitEventKind kind,
        decimal units = 1_000m,
        decimal navPerUnit = 1m,
        decimal poolValuePreMoney = 1_050m,
        Money? cash = null,
        DateOnly? settledOn = null,
        DateOnly? occurredOn = null,
        string? notes = null,
        string poolCurrency = Currency,
        Guid? poolId = null,
        Guid? participantId = null,
        bool participantIsOwner = false) =>
        PoolUnitEvent.Create(
            poolId ?? PoolId,
            participantId ?? ParticipantId,
            kind,
            occurredOn ?? EventDate,
            units,
            navPerUnit,
            poolValuePreMoney,
            cash,
            settledOn,
            notes,
            poolCurrency,
            Inception,
            participantIsOwner,
            Clock());

    /// <summary>The canonical happy-path shape for each kind.</summary>
    private static Result<PoolUnitEvent> CreateValid(PoolUnitEventKind kind) => kind switch
    {
        PoolUnitEventKind.Seed => Create(
            PoolUnitEventKind.Seed,
            units: 1_050m,
            navPerUnit: 1m,
            poolValuePreMoney: 0m,
            occurredOn: Inception,
            participantIsOwner: true),
        PoolUnitEventKind.Subscription => Create(
            PoolUnitEventKind.Subscription,
            units: 1_000m,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate),
        PoolUnitEventKind.Redemption => Create(
            PoolUnitEventKind.Redemption,
            units: 1_000m,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate),
        PoolUnitEventKind.Distribution => Create(
            PoolUnitEventKind.Distribution,
            units: 100m,
            cash: new Money(100m, Currency),
            settledOn: null),
        PoolUnitEventKind.CostShare => Create(PoolUnitEventKind.CostShare, units: 5m, navPerUnit: 2m),
        _ => Create(PoolUnitEventKind.CostRecovery, units: 5m, navPerUnit: 2m),
    };

    // ---- Shape ------------------------------------------------------------

    [Fact]
    public void Create_Subscription_Succeeds()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            units: 1_000m,
            navPerUnit: 1m,
            poolValuePreMoney: 1_050m,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate,
            notes: "  Ion wires in  ");

        result.IsSuccess.Should().BeTrue();
        PoolUnitEvent unitEvent = result.Value;
        unitEvent.Id.Should().NotBe(Guid.Empty);
        unitEvent.PoolId.Should().Be(PoolId);
        unitEvent.ParticipantId.Should().Be(ParticipantId);
        unitEvent.Kind.Should().Be(PoolUnitEventKind.Subscription);
        unitEvent.OccurredOn.Should().Be(EventDate);
        unitEvent.Units.Should().Be(1_000m);
        unitEvent.NavPerUnit.Should().Be(1m);
        unitEvent.PoolValuePreMoney.Should().Be(1_050m);
        unitEvent.Cash.Should().Be(new Money(1_000m, Currency));
        unitEvent.SettledOn.Should().Be(EventDate);
        unitEvent.MovementTransactionId.Should().BeNull();
        unitEvent.Notes.Should().Be("Ion wires in");
    }

    [Theory]
    [InlineData(PoolUnitEventKind.Seed, 1)]
    [InlineData(PoolUnitEventKind.Subscription, 1)]
    [InlineData(PoolUnitEventKind.CostRecovery, 1)]
    [InlineData(PoolUnitEventKind.Redemption, -1)]
    [InlineData(PoolUnitEventKind.Distribution, -1)]
    [InlineData(PoolUnitEventKind.CostShare, -1)]
    public void UnitsDelta_TakesItsSignFromTheKind(PoolUnitEventKind kind, int expectedSign)
    {
        PoolUnitEvent unitEvent = CreateValid(kind).Value;

        // Units itself is always a positive magnitude.
        unitEvent.Units.Should().BePositive();
        unitEvent.UnitsDelta.Should().Be(expectedSign * unitEvent.Units);
    }

    [Fact]
    public void Create_UndefinedKind_ReturnsUnitEventKindInvalid()
    {
        Result<PoolUnitEvent> result = Create((PoolUnitEventKind)99, cash: new Money(1_000m, Currency));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.UnitEventKindInvalid);
    }

    [Fact]
    public void Create_EmptyPoolId_ReturnsPoolNotFound()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate,
            poolId: Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NotFound(Guid.Empty));
    }

    [Fact]
    public void Create_EmptyParticipantId_ReturnsParticipantNotFound()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate,
            participantId: Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantNotFound(Guid.Empty));
    }

    // ---- Magnitude / date invariants ---------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1_000)]
    public void Create_NonPositiveUnits_ReturnsUnitsMustBePositive(decimal units)
    {
        // Magnitudes only - the direction lives in Kind, so a signed Units
        // would double-count it.
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            units: units,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.UnitsMustBePositive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveNav_ReturnsNavPerUnitMustBePositive(decimal nav)
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            navPerUnit: nav,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NavPerUnitMustBePositive);
    }

    [Fact]
    public void Create_NegativePoolValue_ReturnsPoolValueCannotBeNegative()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            poolValuePreMoney: -1m,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.PoolValueCannotBeNegative);
    }

    [Fact]
    public void Create_OccurredOnInFuture_ReturnsUnitEventDateInFuture()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: Today.AddDays(1),
            occurredOn: Today.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.UnitEventDateInFuture);
    }

    [Fact]
    public void Create_OccurredOnToday_Succeeds()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: Today,
            occurredOn: Today);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_OccurredOnBeforeInception_ReturnsUnitEventBeforeInception()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: Inception,
            occurredOn: Inception.AddDays(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.UnitEventBeforeInception);
    }

    [Fact]
    public void Create_NotesOver500Chars_ReturnsNotesTooLong()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate,
            notes: new string('n', 501));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NotesTooLong);
    }

    // ---- Cash <-> units reconciliation -------------------------------------

    [Theory]
    [InlineData(PoolUnitEventKind.Subscription)]
    [InlineData(PoolUnitEventKind.Redemption)]
    [InlineData(PoolUnitEventKind.Distribution)]
    public void Create_CashKindWithoutCash_ReturnsCashRequired(PoolUnitEventKind kind)
    {
        Result<PoolUnitEvent> result = Create(kind, cash: null, settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashRequired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1_000)]
    public void Create_NonPositiveCash_ReturnsCashMustBePositive(decimal amount)
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Redemption,
            cash: new Money(amount, Currency),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashMustBePositive);
    }

    [Fact]
    public void Create_CashInAnotherCurrency_ReturnsCashCurrencyMismatch()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, "EUR"),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashCurrencyMismatch);
    }

    [Fact]
    public void Create_CashThatDoesNotMatchUnitsTimesNav_ReturnsCashDoesNotMatchUnits()
    {
        // The demonstrated real-world slip: someone types the account's LEVEL
        // into an amount field. 500 units at NAV 2 is 1,000 - not 1,500.
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            units: 500m,
            navPerUnit: 2m,
            cash: new Money(1_500m, Currency),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashDoesNotMatchUnits);
    }

    [Fact]
    public void Create_CashOffByExactlyHalfACent_Succeeds()
    {
        // units x nav = 100.005, cash rounds to 100.00 - exactly the tolerance.
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            units: 100.005m,
            navPerUnit: 1m,
            cash: new Money(100.00m, Currency),
            settledOn: EventDate);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_CashOffByMoreThanHalfACent_ReturnsCashDoesNotMatchUnits()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            units: 100.0051m,
            navPerUnit: 1m,
            cash: new Money(100.00m, Currency),
            settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashDoesNotMatchUnits);
    }

    [Fact]
    public void Create_TwelveDecimalPlaceNav_IsKeptVerbatim()
    {
        // The whole point of numeric(28,12): at 2dp this NAV would quantize to
        // 1.00 and the invariance identity would stop holding.
        const decimal nav = 1.000001234567m;

        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            units: 1_000m,
            navPerUnit: nav,
            cash: new Money(1_000.00m, Currency),
            settledOn: EventDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.NavPerUnit.Should().Be(nav);
        result.Value.NavPerUnit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("1.000001234567");
    }

    // ---- Seed carve-out -----------------------------------------------------

    [Fact]
    public void Create_Seed_CarriesNoCashAndNoTransaction()
    {
        // The owner's EXISTING balance simply becomes units. Synthesizing a
        // 1,050 Income leg to "fund" it would double the account.
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            units: 1_050m,
            navPerUnit: 1m,
            poolValuePreMoney: 0m,
            occurredOn: Inception,
            participantIsOwner: true);

        result.IsSuccess.Should().BeTrue();
        PoolUnitEvent seed = result.Value;
        seed.Cash.Should().BeNull();
        seed.SettledOn.Should().BeNull();
        seed.MovementTransactionId.Should().BeNull();
        seed.NavPerUnit.Should().Be(1m);
        seed.PoolValuePreMoney.Should().Be(0m);
        seed.OccurredOn.Should().Be(Inception);
        seed.UnitsDelta.Should().Be(1_050m);
    }

    [Fact]
    public void Create_SeedForNonOwner_ReturnsSeedMustBeOwner()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            poolValuePreMoney: 0m,
            occurredOn: Inception,
            participantIsOwner: false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SeedMustBeOwner);
    }

    [Fact]
    public void Create_SeedDatedAfterInception_ReturnsSeedDateMustMatchInception()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            poolValuePreMoney: 0m,
            occurredOn: Inception.AddDays(1),
            participantIsOwner: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SeedDateMustMatchInception);
    }

    [Fact]
    public void Create_SeedAtNavOtherThanOne_ReturnsSeedNavMustBeOne()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            navPerUnit: 1.5m,
            poolValuePreMoney: 0m,
            occurredOn: Inception,
            participantIsOwner: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SeedNavMustBeOne);
    }

    [Fact]
    public void Create_SeedWithNonZeroPreMoneyValue_ReturnsSeedPoolValueMustBeZero()
    {
        // Pre-money is the value BEFORE the event; before the seed the pool
        // holds nothing. Post-money is units x 1.
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            poolValuePreMoney: 1_050m,
            occurredOn: Inception,
            participantIsOwner: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SeedPoolValueMustBeZero);
    }

    [Fact]
    public void Create_SeedWithCash_ReturnsCashNotAllowed()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            poolValuePreMoney: 0m,
            cash: new Money(1_050m, Currency),
            occurredOn: Inception,
            participantIsOwner: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashNotAllowed);
    }

    [Fact]
    public void Create_SeedWithSettlementDate_ReturnsSettledOnNotAllowed()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Seed,
            poolValuePreMoney: 0m,
            settledOn: Inception,
            occurredOn: Inception,
            participantIsOwner: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettledOnNotAllowed);
    }

    [Fact]
    public void LinkMovementTransaction_OnSeed_ReturnsMovementTransactionNotAllowed()
    {
        PoolUnitEvent seed = CreateValid(PoolUnitEventKind.Seed).Value;

        Result result = seed.LinkMovementTransaction(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.MovementTransactionNotAllowed);
        seed.MovementTransactionId.Should().BeNull();
    }

    // ---- Two-phase distribution --------------------------------------------

    [Fact]
    public void Create_Distribution_MayBeUnpaid()
    {
        // Close at month-end, pay at the start of the next month. Until then
        // the cash is still in the account and NAV must net it out.
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Distribution,
            units: 100m,
            cash: new Money(100m, Currency),
            settledOn: null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cash.Should().Be(new Money(100m, Currency));
        result.Value.SettledOn.Should().BeNull();
        result.Value.MovementTransactionId.Should().BeNull();
    }

    [Theory]
    [InlineData(PoolUnitEventKind.Subscription)]
    [InlineData(PoolUnitEventKind.Redemption)]
    public void Create_CashKindOtherThanDistributionWithoutSettlement_ReturnsSettledOnRequired(
        PoolUnitEventKind kind)
    {
        // Only a distribution may sit owed-but-unpaid.
        Result<PoolUnitEvent> result = Create(kind, cash: new Money(1_000m, Currency), settledOn: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettledOnRequired);
    }

    [Fact]
    public void Create_SettlementBeforeTheEvent_ReturnsSettlementBeforeEvent()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: EventDate.AddDays(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettlementBeforeEvent);
    }

    [Fact]
    public void Create_SettlementInFuture_ReturnsSettlementDateInFuture()
    {
        Result<PoolUnitEvent> result = Create(
            PoolUnitEventKind.Subscription,
            cash: new Money(1_000m, Currency),
            settledOn: Today.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettlementDateInFuture);
    }

    [Fact]
    public void Settle_MarksTheDistributionPaid()
    {
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;
        var transactionId = Guid.CreateVersion7();
        DateOnly paidOn = EventDate.AddDays(1);

        Result result = distribution.Settle(paidOn, transactionId, Clock());

        result.IsSuccess.Should().BeTrue();
        distribution.SettledOn.Should().Be(paidOn);
        distribution.MovementTransactionId.Should().Be(transactionId);
    }

    [Fact]
    public void Settle_Twice_ReturnsDistributionAlreadySettled()
    {
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;
        distribution.Settle(EventDate, Guid.CreateVersion7(), Clock());

        Result result = distribution.Settle(EventDate, Guid.CreateVersion7(), Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.DistributionAlreadySettled);
    }

    [Fact]
    public void Settle_BeforeTheCloseDate_ReturnsSettlementBeforeEvent()
    {
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;

        Result result = distribution.Settle(EventDate.AddDays(-1), Guid.CreateVersion7(), Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettlementBeforeEvent);
        distribution.SettledOn.Should().BeNull();
    }

    [Fact]
    public void Settle_InTheFuture_ReturnsSettlementDateInFuture()
    {
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;

        Result result = distribution.Settle(Today.AddDays(1), Guid.CreateVersion7(), Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettlementDateInFuture);
    }

    [Fact]
    public void Settle_WithEmptyTransactionId_ReturnsMovementTransactionRequired()
    {
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;

        Result result = distribution.Settle(EventDate, Guid.Empty, Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.MovementTransactionRequired);
    }

    [Fact]
    public void Settle_OnASubscription_ReturnsSettleNotApplicable()
    {
        PoolUnitEvent subscription = CreateValid(PoolUnitEventKind.Subscription).Value;

        Result result = subscription.Settle(EventDate, Guid.CreateVersion7(), Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettleNotApplicable);
    }

    [Fact]
    public void LinkMovementTransaction_OnADistribution_ReturnsDistributionMustSettle()
    {
        // A distribution with a transaction but no settlement date still reads
        // as unpaid to the NAV calculation, so the payout gets counted twice.
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;

        Result result = distribution.LinkMovementTransaction(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.DistributionMustSettle);
        distribution.MovementTransactionId.Should().BeNull();
    }

    [Fact]
    public void ClearMovementTransaction_OnASettledDistribution_RevertsItToUnpaid()
    {
        // Deleting the payment transaction puts the cash back in the account,
        // so the payout is owed again.
        PoolUnitEvent distribution = CreateValid(PoolUnitEventKind.Distribution).Value;
        distribution.Settle(EventDate, Guid.CreateVersion7(), Clock());

        distribution.ClearMovementTransaction();

        distribution.MovementTransactionId.Should().BeNull();
        distribution.SettledOn.Should().BeNull();
    }

    [Fact]
    public void ClearMovementTransaction_OnASubscription_KeepsItsSettlementDate()
    {
        // A subscription's settlement date is not a payment-pending marker, so
        // dropping a dead transaction link must not rewrite it.
        PoolUnitEvent subscription = CreateValid(PoolUnitEventKind.Subscription).Value;
        subscription.LinkMovementTransaction(Guid.CreateVersion7()).IsSuccess.Should().BeTrue();

        subscription.ClearMovementTransaction();

        subscription.MovementTransactionId.Should().BeNull();
        subscription.SettledOn.Should().Be(EventDate);
    }

    [Fact]
    public void ClearMovementTransaction_IsIdempotent()
    {
        PoolUnitEvent subscription = CreateValid(PoolUnitEventKind.Subscription).Value;

        subscription.ClearMovementTransaction();
        subscription.ClearMovementTransaction();

        subscription.MovementTransactionId.Should().BeNull();
    }

    [Theory]
    [InlineData(PoolUnitEventKind.Subscription)]
    [InlineData(PoolUnitEventKind.Redemption)]
    public void LinkMovementTransaction_OnAnImmediateCashKind_Succeeds(PoolUnitEventKind kind)
    {
        PoolUnitEvent unitEvent = CreateValid(kind).Value;
        var transactionId = Guid.CreateVersion7();

        Result result = unitEvent.LinkMovementTransaction(transactionId);

        result.IsSuccess.Should().BeTrue();
        unitEvent.MovementTransactionId.Should().Be(transactionId);
    }

    [Fact]
    public void LinkMovementTransaction_WithEmptyId_ReturnsMovementTransactionRequired()
    {
        PoolUnitEvent subscription = CreateValid(PoolUnitEventKind.Subscription).Value;

        Result result = subscription.LinkMovementTransaction(Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.MovementTransactionRequired);
    }

    // ---- Cost transfer (CostShare / CostRecovery) ---------------------------

    [Theory]
    [InlineData(PoolUnitEventKind.CostShare)]
    [InlineData(PoolUnitEventKind.CostRecovery)]
    public void Create_CostEvent_HasNoCashLegAndNoSettlement(PoolUnitEventKind kind)
    {
        // The owner paid a server bill out of pocket; the friends reimburse in
        // UNITS. The money never enters the account, so a cash leg here would
        // mint value out of nothing.
        Result<PoolUnitEvent> result = Create(kind, units: 5m, navPerUnit: 2m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cash.Should().BeNull();
        result.Value.SettledOn.Should().BeNull();
        result.Value.MovementTransactionId.Should().BeNull();
    }

    [Theory]
    [InlineData(PoolUnitEventKind.CostShare)]
    [InlineData(PoolUnitEventKind.CostRecovery)]
    public void Create_CostEventWithCash_ReturnsCashNotAllowed(PoolUnitEventKind kind)
    {
        Result<PoolUnitEvent> result = Create(kind, units: 5m, navPerUnit: 2m, cash: new Money(10m, Currency));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CashNotAllowed);
    }

    [Theory]
    [InlineData(PoolUnitEventKind.CostShare)]
    [InlineData(PoolUnitEventKind.CostRecovery)]
    public void Create_CostEventWithSettlementDate_ReturnsSettledOnNotAllowed(PoolUnitEventKind kind)
    {
        Result<PoolUnitEvent> result = Create(kind, units: 5m, navPerUnit: 2m, settledOn: EventDate);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.SettledOnNotAllowed);
    }

    [Theory]
    [InlineData(PoolUnitEventKind.CostShare)]
    [InlineData(PoolUnitEventKind.CostRecovery)]
    public void LinkMovementTransaction_OnACostEvent_ReturnsMovementTransactionNotAllowed(
        PoolUnitEventKind kind)
    {
        PoolUnitEvent unitEvent = CreateValid(kind).Value;

        Result result = unitEvent.LinkMovementTransaction(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.MovementTransactionNotAllowed);
    }

    [Fact]
    public void CostTransfer_IsUnitNeutralAcrossTheTwoLegs()
    {
        // Two friends give up 2.5 units each at NAV 2 (USD 10 of cost), the
        // owner picks up 5. Total units, pool value and NAV are all unchanged.
        const decimal nav = 2m;
        var friendA = Guid.CreateVersion7();
        var friendB = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();

        PoolUnitEvent shareA = Create(
            PoolUnitEventKind.CostShare, units: 2.5m, navPerUnit: nav, participantId: friendA).Value;
        PoolUnitEvent shareB = Create(
            PoolUnitEventKind.CostShare, units: 2.5m, navPerUnit: nav, participantId: friendB).Value;
        PoolUnitEvent recovery = Create(
            PoolUnitEventKind.CostRecovery, units: 5m, navPerUnit: nav, participantId: owner).Value;

        (shareA.UnitsDelta + shareB.UnitsDelta + recovery.UnitsDelta).Should().Be(0m);

        // Same NAV, same date on every leg - that is what keeps NAV unchanged.
        shareA.NavPerUnit.Should().Be(nav);
        shareB.NavPerUnit.Should().Be(nav);
        recovery.NavPerUnit.Should().Be(nav);
        shareA.OccurredOn.Should().Be(recovery.OccurredOn);
        shareB.OccurredOn.Should().Be(recovery.OccurredOn);
    }
}
