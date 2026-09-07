using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Application.Features.Pools.AddPoolParticipant;
using MoneyManagement.Application.Features.Pools.ArchivePool;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.DeletePoolEvent;
using MoneyManagement.Application.Features.Pools.RecordCostReimbursement;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// Drives the pools write slice end to end against
/// <see cref="FakeApplicationDbContext"/>.
/// <para>
/// Deliberately builds state by running the REAL handlers rather than by
/// hand-crafting <see cref="PoolUnitEvent"/> rows: the properties under test
/// (NAV invariance, the high-water mark, the two-phase distribution) are
/// properties of the handlers' arithmetic, and a fixture that computed the units
/// itself would only be testing the fixture.
/// </para>
/// <para>
/// <see cref="FakeDbSet{T}"/> mutates the list it was constructed with, so rows
/// added by one handler are visible to the next — the harness can therefore run
/// a whole month against one context.
/// </para>
/// </summary>
internal sealed class PoolHarness
{
    public const string Currency = "USD";

    private PoolHarness(IApplicationDbContext db, Account account, MutableClock clock)
    {
        Db = db;
        Account = account;
        Clock = clock;
        Fx = FakeFxConverter.Identity();
    }

    public IApplicationDbContext Db { get; }

    public Account Account { get; }

    public MutableClock Clock { get; }

    public IFxConverter Fx { get; }

    public DateOnly Today => DateOnly.FromDateTime(Clock.UtcNow);

    public Guid PoolId { get; private set; }

    public Guid OwnerId { get; private set; }

    /// <param name="now">
    /// Fixed "now"; every pricing command dates itself here. The default sits
    /// WELL IN THE PAST on purpose: <c>Transaction.Create</c> judges its
    /// no-future-dates rule against the real <c>DateTime.UtcNow</c>, not the
    /// injected clock, so a harness that starts at the real today cannot
    /// <see cref="Advance"/> at all — and the two-phase distribution can only be
    /// exercised by moving the clock between close and pay.
    /// </param>
    /// <param name="openingBalance">The account's opening anchor, in <see cref="Currency"/>.</param>
    /// <param name="type">Account type. Defaults to an adjustment-eligible one.</param>
    /// <param name="extraAccounts">
    /// Other accounts the test needs — a destination for the two-leg
    /// redemption, or a second pooled account for the guard tests.
    /// </param>
    public static PoolHarness Create(
        DateTime? now = null,
        decimal openingBalance = 1_050m,
        AccountType type = AccountType.CryptoExchange,
        params Account[] extraAccounts)
    {
        DateTime utcNow = now ?? new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var clock = new MutableClock(utcNow);

        Result<Account> account = Account.Create(
            "Binance",
            type,
            new Money(openingBalance, Currency),
            new DateOnly(2026, 1, 1),
            notes: null);

        account.IsSuccess.Should().BeTrue();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account.Value, .. extraAccounts]);

        return new PoolHarness(db, account.Value, clock);
    }

    /// <summary>Advances the fixed clock — used by the close/settle timing tests.</summary>
    public void Advance(int days) => Clock.Advance(days);

    public Task<Result<CreatePoolResponse>> CreatePoolAsync(
        DateOnly? inceptionDate = null,
        decimal? poolValueAtInception = 1_092m,
        string ownerName = "Me",
        IReadOnlyList<BackdatedSubscription>? backfills = null,
        Guid? accountId = null,
        string currency = Currency)
    {
        var handler = new CreatePoolCommandHandler(Db, Fx, Clock);

        return handler.Handle(
            new CreatePoolCommand(
                accountId ?? Account.Id,
                "Binance pool",
                currency,
                inceptionDate ?? Today,
                ownerName,
                poolValueAtInception,
                Notes: null,
                backfills),
            CancellationToken.None);
    }

    /// <summary>Creates the pool and remembers its ids, failing loudly if it did not work.</summary>
    public async Task<CreatePoolResponse> SeedPoolAsync(
        DateOnly? inceptionDate = null,
        decimal? poolValueAtInception = 1_092m,
        IReadOnlyList<BackdatedSubscription>? backfills = null)
    {
        Result<CreatePoolResponse> result =
            await CreatePoolAsync(inceptionDate, poolValueAtInception, backfills: backfills);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        PoolId = result.Value.Id;
        OwnerId = result.Value.OwnerParticipantId;
        return result.Value;
    }

    public async Task<Guid> AddParticipantAsync(string name, DateOnly? joinedOn = null)
    {
        var handler = new AddPoolParticipantCommandHandler(Db, Clock);

        Result<AddPoolParticipantResponse> result = await handler.Handle(
            new AddPoolParticipantCommand(PoolId, name, joinedOn ?? Today),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value.Id;
    }

    public Task<Result<RecordSubscriptionResponse>> SubscribeAsync(
        Guid participantId,
        decimal poolValueNow,
        decimal cash,
        string? notes = null) =>
        new RecordSubscriptionCommandHandler(Db, Fx, Clock).Handle(
            new RecordSubscriptionCommand(PoolId, participantId, poolValueNow, cash, notes),
            CancellationToken.None);

    /// <param name="isFullWindDown">
    /// The opt-in "yes, this really is the whole pool" acknowledgement. See
    /// <c>RecordRedemptionCommand.IsFullWindDown</c>.
    /// </param>
    public Task<Result<RecordRedemptionResponse>> RedeemAsync(
        Guid participantId,
        decimal poolValueNow,
        decimal cash,
        Guid? destinationAccountId = null,
        string? notes = null,
        bool isFullWindDown = false) =>
        new RecordRedemptionCommandHandler(Db, Fx, Clock).Handle(
            new RecordRedemptionCommand(
                PoolId,
                participantId,
                poolValueNow,
                cash,
                destinationAccountId,
                notes,
                isFullWindDown),
            CancellationToken.None);

    public Task<Result<CloseDistributionResponse>> CloseAsync(
        decimal poolValueNow,
        IReadOnlyList<DistributionPayout>? payouts = null,
        string? notes = null) =>
        new CloseDistributionCommandHandler(Db, Fx, Clock).Handle(
            new CloseDistributionCommand(PoolId, poolValueNow, payouts, notes),
            CancellationToken.None);

    public Task<Result<SettleDistributionResponse>> SettleAsync(
        Guid eventId,
        DateOnly? settledOn = null,
        string? notes = null) =>
        new SettleDistributionCommandHandler(Db, Fx, Clock).Handle(
            new SettleDistributionCommand(PoolId, eventId, settledOn, notes),
            CancellationToken.None);

    public Task<Result<RecordCostReimbursementResponse>> ReimburseCostAsync(
        decimal amount,
        decimal? poolValueNow = null,
        string? notes = null) =>
        new RecordCostReimbursementCommandHandler(Db, Fx, Clock).Handle(
            new RecordCostReimbursementCommand(PoolId, amount, poolValueNow, notes),
            CancellationToken.None);

    public Task<Result> DeleteEventAsync(Guid eventId) =>
        new DeletePoolEventCommandHandler(
                Db,
                Fx,
                NullLogger<DeletePoolEventCommandHandler>.Instance)
            .Handle(new DeletePoolEventCommand(PoolId, eventId), CancellationToken.None);

    public Task<Result> ArchivePoolAsync() =>
        new ArchivePoolCommandHandler(Db).Handle(new ArchivePoolCommand(PoolId), CancellationToken.None);

    /// <summary>
    /// Folds the pool exactly the way the read slice and the ownership source
    /// will: register over every participant and event, priced against the
    /// account's derived balance. Anything asserted through this is asserted
    /// against the same arithmetic production will use.
    /// </summary>
    public async Task<PoolSnapshot> SnapshotAsync(DateOnly? asOf = null)
    {
        DateOnly date = asOf ?? Today;

        List<PoolParticipant> participants = await Db.PoolParticipants
            .Where(p => p.PoolId == PoolId)
            .ToListAsync();

        List<PoolUnitEvent> events = await Db.PoolUnitEvents
            .Where(e => e.PoolId == PoolId)
            .ToListAsync();

        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(Db, CancellationToken.None);

        return PoolUnitRegister
            .Create(participants, events)
            .SnapshotAsOf(date, ledger.NativeBalanceAsOf(Account, date));
    }

    /// <summary>The account's derived balance — opening anchor plus every live movement.</summary>
    public async Task<decimal> DerivedBalanceAsync(DateOnly? asOf = null)
    {
        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(Db, CancellationToken.None);
        return ledger.NativeBalanceAsOf(Account, asOf ?? Today);
    }
}
