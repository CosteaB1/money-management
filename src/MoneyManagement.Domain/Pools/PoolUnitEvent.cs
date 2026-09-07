using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Pools;

/// <summary>
/// One dated movement of units for one participant of one <see cref="Pool"/> —
/// the pool's entire ledger. Units are minted or burned at the prevailing NAV,
/// which is precisely why NAV survives the operation unchanged.
///
/// <para>
/// <b><see cref="Units"/> and <see cref="NavPerUnit"/> are plain
/// <see cref="decimal"/>s stored at <c>numeric(28,12)</c> — NOT
/// <see cref="Money"/>, NOT a complex property.</b> They follow
/// <c>FxRateConfiguration</c>'s precision reasoning, not the repo-wide
/// <c>numeric(18,2)</c> money convention. At 2dp a NAV near 1.0 quantizes about
/// 0.1% per event per investor and the invariance identity
/// (<c>Σ participantUnits × nav == poolValue</c>) simply stops holding. Changing
/// the scale later means a second migration over live financial data, so it is
/// fixed here. <see cref="Cash"/> and <see cref="PoolValuePreMoney"/> stay
/// money-shaped at <c>numeric(18,2)</c> — they are amounts a bank actually
/// moved.
/// </para>
///
/// <para>
/// <b><see cref="NavPerUnit"/> is an audit record, never an input to the
/// ownership fraction.</b> The fraction is
/// <c>ownerUnits ÷ totalUnits</c> — unit counts only. A NAV later found to be
/// wrong therefore misprices nothing retroactively: every participant's share
/// stays internally consistent, and only the historical price commentary is
/// off. Do not "improve" the ownership source by folding NAV into it.
/// </para>
///
/// <para>
/// <b>Why <see cref="PoolValuePreMoney"/> is a bare decimal while
/// <see cref="Cash"/> is a <see cref="Money"/>:</b> pre-money value is
/// denominated in <see cref="Pool.Currency"/> by definition, so a second
/// currency column could only ever drift out of agreement with the pool. Cash
/// keeps its currency because it pairs with a real transaction row that carries
/// one, which makes <c>Cash.Currency == pool.Currency</c> a check worth being
/// able to make.
/// </para>
/// </summary>
public sealed class PoolUnitEvent : Entity
{
    public const int NotesMaxLength = 500;

    /// <summary>
    /// Half of the smallest value <c>numeric(28,12)</c> can represent. Anything
    /// below it is storage rounding, not a stake — used by the "still holds
    /// units" guards so an exited participant isn't blocked forever by a
    /// twelfth-decimal-place crumb.
    /// </summary>
    public const decimal UnitsDustTolerance = 0.0000000000005m;

    /// <summary>
    /// How far <c>units × navPerUnit</c> may sit from the cash actually moved:
    /// half a cent. Units carry 12dp and cash carries 2, so an exact match is
    /// impossible; anything larger is a typo, not rounding.
    /// </summary>
    public const decimal CashReconciliationTolerance = 0.005m;

    // EF Core
    private PoolUnitEvent()
    {
    }

    private PoolUnitEvent(
        Guid id,
        Guid poolId,
        Guid participantId,
        PoolUnitEventKind kind,
        DateOnly occurredOn,
        decimal units,
        decimal navPerUnit,
        decimal poolValuePreMoney,
        Money? cash,
        DateOnly? settledOn,
        string? notes) : base(id)
    {
        PoolId = poolId;
        ParticipantId = participantId;
        Kind = kind;
        OccurredOn = occurredOn;
        Units = units;
        NavPerUnit = navPerUnit;
        PoolValuePreMoney = poolValuePreMoney;
        CashValue = cash?.Amount;
        CashCurrency = cash?.Currency;
        SettledOn = settledOn;
        MovementTransactionId = null;
        Notes = notes;
    }

    public Guid PoolId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public PoolUnitEventKind Kind { get; private set; }

    /// <summary>
    /// The day the units moved. For a <see cref="PoolUnitEventKind.Distribution"/>
    /// this is the CLOSE date, which is generally not the day the money left —
    /// see <see cref="SettledOn"/>.
    /// </summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>Always a positive magnitude; <see cref="Kind"/> carries the direction.</summary>
    public decimal Units { get; private set; }

    /// <summary>The price the units were struck at. Audit only — see the type remarks.</summary>
    public decimal NavPerUnit { get; private set; }

    /// <summary>
    /// The pool's total value immediately BEFORE this event, in
    /// <see cref="Pool.Currency"/>. Zero for a <see cref="PoolUnitEventKind.Seed"/>
    /// (the pool holds nothing before it exists).
    /// </summary>
    public decimal PoolValuePreMoney { get; private set; }

    // Scalar columns backing the nullable Cash. EF Core does not map a nullable
    // ComplexProperty cleanly, so the two primitives are persisted directly and
    // recombined on read - exactly the SavingsGoal.ManualSavedAmount pattern.
    // They are set as a pair: both NULL for the no-cash kinds, both populated
    // otherwise.
    private decimal? CashValue { get; set; }

    private string? CashCurrency { get; set; }

    /// <summary>
    /// The money that actually moved, in the pool's currency. <c>null</c> for
    /// <see cref="PoolUnitEventKind.Seed"/>, <see cref="PoolUnitEventKind.CostShare"/>
    /// and <see cref="PoolUnitEventKind.CostRecovery"/>, which move units only.
    /// </summary>
    public Money? Cash =>
        CashValue is decimal value && CashCurrency is string currency
            ? new Money(value, currency)
            : null;

    /// <summary>
    /// The day the cash physically moved.
    /// <para>
    /// <b>Null on an un-paid <see cref="PoolUnitEventKind.Distribution"/> — and
    /// only there.</b> The month closes at month-end but the USDT leaves at the
    /// start of the next month, so between the two the payout is owed but the
    /// cash is still sitting in the account. Readers must compute NAV as
    /// <c>accountBalance − unpaidDistributionCash</c>; skip that and the money
    /// counts as pool value a second time and the friends get paid twice on the
    /// same profit, every month.
    /// </para>
    /// </summary>
    public DateOnly? SettledOn { get; private set; }

    /// <summary>
    /// The synthesized money-movement row, when there is one. Null forever for
    /// the three no-cash kinds, and null on a distribution until it is paid.
    /// Cleared (never cascaded) if the transaction is deleted, so the ledger
    /// entry itself always survives.
    /// </summary>
    public Guid? MovementTransactionId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// The signed effect on the participant's balance. Seeds, subscriptions and
    /// cost recoveries add; redemptions, distributions and cost shares subtract.
    /// </summary>
    public decimal UnitsDelta => KindIncreasesUnits(Kind) ? Units : -Units;

    /// <summary>Whether a kind moves real money (and therefore needs a cash leg + transaction).</summary>
    public static bool KindCarriesCash(PoolUnitEventKind kind) =>
        kind is PoolUnitEventKind.Subscription
            or PoolUnitEventKind.Redemption
            or PoolUnitEventKind.Distribution;

    /// <summary>Whether a kind adds units rather than removing them.</summary>
    public static bool KindIncreasesUnits(PoolUnitEventKind kind) =>
        kind is PoolUnitEventKind.Seed
            or PoolUnitEventKind.Subscription
            or PoolUnitEventKind.CostRecovery;

    /// <summary>
    /// The single entry point for every kind — the carve-outs are enforced here
    /// rather than offered as separate factories, so a caller cannot route
    /// around them.
    /// </summary>
    /// <param name="poolId">Owning pool.</param>
    /// <param name="participantId">Whose units moved.</param>
    /// <param name="kind">What happened. Drives almost every rule below.</param>
    /// <param name="occurredOn">Units-movement date (the CLOSE date for a distribution).</param>
    /// <param name="units">Positive magnitude of units moved.</param>
    /// <param name="navPerUnit">Positive price the units were struck at. Exactly 1 for a seed.</param>
    /// <param name="poolValuePreMoney">Pool value before the event; 0 for a seed.</param>
    /// <param name="cash">Money moved. Required for the cash kinds, forbidden otherwise.</param>
    /// <param name="settledOn">
    /// When the cash moved. Required for subscriptions/redemptions, forbidden on
    /// the no-cash kinds, and optional (null = unpaid) only for a distribution.
    /// </param>
    /// <param name="notes">Free text, optional.</param>
    /// <param name="poolCurrency">The pool's currency — the caller supplies the parent's invariant.</param>
    /// <param name="poolInceptionDate">The pool's inception date — likewise.</param>
    /// <param name="participantIsOwner">Whether the participant is the owner row. Only a seed cares.</param>
    /// <param name="clock">Injected so the "no future dates" guards are deterministic in tests.</param>
    public static Result<PoolUnitEvent> Create(
        Guid poolId,
        Guid participantId,
        PoolUnitEventKind kind,
        DateOnly occurredOn,
        decimal units,
        decimal navPerUnit,
        decimal poolValuePreMoney,
        Money? cash,
        DateOnly? settledOn,
        string? notes,
        string poolCurrency,
        DateOnly poolInceptionDate,
        bool participantIsOwner,
        IDateTimeProvider clock)
    {
        if (poolId == Guid.Empty)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.NotFound(poolId));
        }

        if (participantId == Guid.Empty)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.ParticipantNotFound(participantId));
        }

        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.UnitEventKindInvalid);
        }

        // Magnitudes, never signed - Kind carries the direction (UnitsDelta).
        if (units <= 0m)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.UnitsMustBePositive);
        }

        if (navPerUnit <= 0m)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.NavPerUnitMustBePositive);
        }

        if (poolValuePreMoney < 0m)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.PoolValueCannotBeNegative);
        }

        // No future-dated events; judged in UTC via the injected clock - same
        // convention as every other dated guard in the domain.
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (occurredOn > today)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.UnitEventDateInFuture);
        }

        if (occurredOn < poolInceptionDate)
        {
            return Result.Failure<PoolUnitEvent>(PoolErrors.UnitEventBeforeInception);
        }

        Result kindValidation = kind == PoolUnitEventKind.Seed
            ? ValidateSeed(occurredOn, navPerUnit, poolValuePreMoney, cash, settledOn, poolInceptionDate, participantIsOwner)
            : ValidateNonSeed(kind, occurredOn, units, navPerUnit, cash, settledOn, poolCurrency, today);

        if (kindValidation.IsFailure)
        {
            return Result.Failure<PoolUnitEvent>(kindValidation.Error);
        }

        Result<string?> notesValidation = ValidateNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<PoolUnitEvent>(notesValidation.Error);
        }

        return new PoolUnitEvent(
            Guid.CreateVersion7(),
            poolId,
            participantId,
            kind,
            occurredOn,
            units,
            navPerUnit,
            poolValuePreMoney,
            cash,
            settledOn,
            notesValidation.Value);
    }

    /// <summary>
    /// Links the synthesized money-movement row for a subscription or
    /// redemption, which settle the moment they are recorded.
    /// <para>
    /// A <see cref="PoolUnitEventKind.Distribution"/> is deliberately rejected:
    /// its transaction and its <see cref="SettledOn"/> must be set in one step
    /// (<see cref="Settle"/>), because a distribution with a transaction but no
    /// settlement date still reads as "unpaid" to the NAV calculation and the
    /// payout gets counted twice.
    /// </para>
    /// </summary>
    public Result LinkMovementTransaction(Guid transactionId)
    {
        if (transactionId == Guid.Empty)
        {
            return Result.Failure(PoolErrors.MovementTransactionRequired);
        }

        if (!KindCarriesCash(Kind))
        {
            return Result.Failure(PoolErrors.MovementTransactionNotAllowed);
        }

        if (Kind == PoolUnitEventKind.Distribution)
        {
            return Result.Failure(PoolErrors.DistributionMustSettle);
        }

        MovementTransactionId = transactionId;
        return Result.Success();
    }

    /// <summary>
    /// Phase two of a distribution: the transfer has physically gone out. Sets
    /// the settlement date and the payment transaction together — the pair is
    /// what stops the cash being double-counted while it waits in the account.
    /// </summary>
    public Result Settle(DateOnly settledOn, Guid movementTransactionId, IDateTimeProvider clock)
    {
        if (Kind != PoolUnitEventKind.Distribution)
        {
            return Result.Failure(PoolErrors.SettleNotApplicable);
        }

        if (SettledOn is not null || MovementTransactionId is not null)
        {
            return Result.Failure(PoolErrors.DistributionAlreadySettled);
        }

        if (movementTransactionId == Guid.Empty)
        {
            return Result.Failure(PoolErrors.MovementTransactionRequired);
        }

        if (settledOn < OccurredOn)
        {
            return Result.Failure(PoolErrors.SettlementBeforeEvent);
        }

        if (settledOn > DateOnly.FromDateTime(clock.UtcNow))
        {
            return Result.Failure(PoolErrors.SettlementDateInFuture);
        }

        SettledOn = settledOn;
        MovementTransactionId = movementTransactionId;
        return Result.Success();
    }

    /// <summary>
    /// Idempotent: drops the transaction link when the underlying row is
    /// (soft-)deleted, so the ledger entry never points at a dead transaction.
    /// <para>
    /// For a distribution this ALSO reverts <see cref="SettledOn"/> to null.
    /// Deleting the payment transaction puts the cash back in the account, so
    /// the payout is unpaid again — leaving it marked settled would let that
    /// same money be distributed a second time.
    /// </para>
    /// </summary>
    public void ClearMovementTransaction()
    {
        MovementTransactionId = null;

        if (Kind == PoolUnitEventKind.Distribution)
        {
            SettledOn = null;
        }
    }

    private static Result ValidateSeed(
        DateOnly occurredOn,
        decimal navPerUnit,
        decimal poolValuePreMoney,
        Money? cash,
        DateOnly? settledOn,
        DateOnly poolInceptionDate,
        bool participantIsOwner)
    {
        // The owner's existing account balance simply BECOMES units. Anyone
        // else joining puts new money in, which is a Subscription.
        if (!participantIsOwner)
        {
            return Result.Failure(PoolErrors.SeedMustBeOwner);
        }

        if (occurredOn != poolInceptionDate)
        {
            return Result.Failure(PoolErrors.SeedDateMustMatchInception);
        }

        if (navPerUnit != 1m)
        {
            return Result.Failure(PoolErrors.SeedNavMustBeOne);
        }

        // Pre-money value is zero by definition: the pool holds nothing until
        // the seed strikes. Post-money is units x 1.
        if (poolValuePreMoney != 0m)
        {
            return Result.Failure(PoolErrors.SeedPoolValueMustBeZero);
        }

        // No cash and no transaction. Synthesizing a 1,050 Income leg to
        // "fund" the seed would double the account - the money is already
        // there.
        if (cash is not null)
        {
            return Result.Failure(PoolErrors.CashNotAllowed);
        }

        if (settledOn is not null)
        {
            return Result.Failure(PoolErrors.SettledOnNotAllowed);
        }

        return Result.Success();
    }

    private static Result ValidateNonSeed(
        PoolUnitEventKind kind,
        DateOnly occurredOn,
        decimal units,
        decimal navPerUnit,
        Money? cash,
        DateOnly? settledOn,
        string poolCurrency,
        DateOnly today)
    {
        if (!KindCarriesCash(kind))
        {
            // CostShare / CostRecovery: a PURE UNIT TRANSFER. The owner paid a
            // pool cost out of their own pocket and the friends reimburse in
            // units - the money never enters the account, so total units, pool
            // value and NAV are all unchanged. A cash leg here would mint value
            // out of nothing.
            if (cash is not null)
            {
                return Result.Failure(PoolErrors.CashNotAllowed);
            }

            return settledOn is not null
                ? Result.Failure(PoolErrors.SettledOnNotAllowed)
                : Result.Success();
        }

        if (cash is not Money money)
        {
            return Result.Failure(PoolErrors.CashRequired);
        }

        if (money.Amount <= 0m)
        {
            return Result.Failure(PoolErrors.CashMustBePositive);
        }

        if (!string.Equals(money.Currency, poolCurrency, StringComparison.Ordinal))
        {
            return Result.Failure(PoolErrors.CashCurrencyMismatch);
        }

        // The cash and the units have to describe the same trade. Units carry
        // 12dp against cash's 2, so allow half a cent of quantization - beyond
        // that somebody typed a balance into an amount field.
        if (Math.Abs(money.Amount - units * navPerUnit) > CashReconciliationTolerance)
        {
            return Result.Failure(PoolErrors.CashDoesNotMatchUnits);
        }

        if (settledOn is not DateOnly settled)
        {
            // Only a distribution may be recorded as owed-but-unpaid.
            return kind == PoolUnitEventKind.Distribution
                ? Result.Success()
                : Result.Failure(PoolErrors.SettledOnRequired);
        }

        if (settled < occurredOn)
        {
            return Result.Failure(PoolErrors.SettlementBeforeEvent);
        }

        return settled > today
            ? Result.Failure(PoolErrors.SettlementDateInFuture)
            : Result.Success();
    }

    private static Result<string?> ValidateNotes(string? notes)
    {
        string? trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > NotesMaxLength)
        {
            return Result.Failure<string?>(PoolErrors.NotesTooLong);
        }

        return Result.Success<string?>(trimmed);
    }
}
