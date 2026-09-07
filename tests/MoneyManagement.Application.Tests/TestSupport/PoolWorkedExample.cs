using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.Pools.SettleDistribution;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// POOLED-CAPITAL.md §6 — "Worked September 2026 example" — replayed through the
/// REAL write handlers, step by step, so every read-side suite can assert against
/// figures that were computed by hand and verified against the live account.
/// <para>
/// <b>The calendar is shifted back three months.</b> <c>Transaction.Create</c>
/// judges its no-future-dates rule against the real <see cref="DateTime.UtcNow"/>
/// rather than the injected clock (see the <c>PoolHarness.Create</c> remarks), so
/// September 8/12/18 2026 cannot be written while the repo's own "today" sits on
/// 2026-09-07. The DAY NUMBERS are kept — 6th, 8th, 12th, 18th, month-end, then
/// the payout at the start of the next month — so each step lines up with the
/// document one for one. Nothing in the arithmetic depends on the month: every
/// figure below is a function of the marks and the cash, not of the calendar.
/// </para>
/// <para>
/// <b>Two-phase distributions, which §6 predates.</b> §6 writes the payout legs
/// on the close date; the implementation does not. A close retires units and
/// records the amount owed with no transaction, and the cash leg is written days
/// later by <c>SettleDistribution</c>. §6's FIGURES are the oracle; the mechanics
/// here are the shipped ones.
/// </para>
/// </summary>
internal sealed class PoolWorkedExample
{
    // ---- the calendar ----------------------------------------------------

    /// <summary>"Sep 6" — the pool is created and the owner's balance becomes units.</summary>
    public static readonly DateOnly Inception = new(2026, 6, 6);

    /// <summary>"Sep 8" — Andrei subscribes 1,000 at a pre-money equal to derived, so no mark is written.</summary>
    public static readonly DateOnly AndreiSubscribes = new(2026, 6, 8);

    /// <summary>"Sep 12" — Bogdan subscribes 1,000 against a real 2,175.68; the mark goes first.</summary>
    public static readonly DateOnly BogdanSubscribes = new(2026, 6, 12);

    /// <summary>"Sep 18" — 400 moves to Bybit. Dilutes the owner only.</summary>
    public static readonly DateOnly OwnerWithdraws = new(2026, 6, 18);

    /// <summary>"Sep 30" — the month closes: units retire, the cash is merely owed.</summary>
    public static readonly DateOnly MonthCloses = new(2026, 6, 30);

    /// <summary>"Oct 2" — the USDT physically leaves, days after the close.</summary>
    public static readonly DateOnly PayoutSettles = new(2026, 7, 2);

    // ---- the money -------------------------------------------------------

    /// <summary>What the app derived on inception day, before the catch-up mark.</summary>
    public const decimal OpeningBalance = 1_050m;

    /// <summary>What Binance really held on inception day.</summary>
    public const decimal PoolValueAtInception = 1_092m;

    public const decimal InceptionMarkDelta = 42m;

    public const decimal AndreiCash = 1_000m;

    public const decimal BogdanCash = 1_000m;

    /// <summary>Binance's real total when Bogdan arrives; 83.68 above the derived 2,092.</summary>
    public const decimal BogdanPreMoney = 2_175.68m;

    public const decimal BogdanMarkDelta = 83.68m;

    public const decimal WithdrawalCash = 400m;

    /// <summary>Binance's real total at the close; 160.14 above the derived 2,775.68.</summary>
    public const decimal ClosePoolValue = 2_935.82m;

    public const decimal CloseMarkDelta = 160.14m;

    public const decimal AndreiPayout = 100m;

    public const decimal BogdanPayout = 57.69m;

    public const decimal TotalPayout = 157.69m;

    // ---- units and NAV, at the numeric(28,12) storage scale ---------------

    public const decimal SeedUnits = 1_092m;

    public const decimal AndreiUnits = 1_000m;

    /// <summary><c>1,000 ÷ 1.04</c>, rounded to 12dp. §6 prints 961.5384615385 at 10dp.</summary>
    public const decimal BogdanUnits = 961.538461538462m;

    /// <summary><c>400 ÷ 1.04</c>, rounded to 12dp. §6 prints 384.6153846154 at 10dp.</summary>
    public const decimal WithdrawnUnits = 384.615384615385m;

    public const decimal OwnerUnits = SeedUnits - WithdrawnUnits;

    public const decimal TotalUnitsAfterWithdrawal = 2_668.923076923077m;

    public const decimal PoolValueAfterWithdrawal = 2_775.68m;

    public const decimal NavAfterWithdrawal = 1.04m;

    /// <summary><c>2,935.82 ÷ 2,668.923076923077</c>. §6 prints 1.1000017293 at 10dp.</summary>
    public const decimal NavAtClose = 1.100001729306m;

    /// <summary><c>100.00 ÷ NavAtClose</c>, 12dp. §6 prints 90.9089480 at 7dp.</summary>
    public const decimal AndreiUnitsRetired = 90.908947991464m;

    /// <summary><c>57.69 ÷ NavAtClose</c>, 12dp. §6 prints 52.4453720 at 7dp.</summary>
    public const decimal BogdanUnitsRetired = 52.445372096276m;

    public const decimal AndreiUnitsAfterClose = AndreiUnits - AndreiUnitsRetired;

    public const decimal BogdanUnitsAfterClose = BogdanUnits - BogdanUnitsRetired;

    /// <summary>
    /// <c>2,668.923076923077 − 90.908947991464 − 52.445372096276</c>.
    /// <para>
    /// §6 prints <b>2,525.5687569231</b>, which is what the same subtraction gives
    /// when the two retirements are taken at the 7dp the document rounds them to.
    /// The column is <c>numeric(28,12)</c>, so the shipped figure carries five more
    /// digits; the two agree to 1e-8 of a unit.
    /// </para>
    /// </summary>
    public const decimal TotalUnitsAfterClose = 2_525.568756835337m;

    public const decimal PoolValueAfterClose = 2_778.13m;

    private readonly PoolHarness _harness;

    private PoolWorkedExample(PoolHarness harness) => _harness = harness;

    public IApplicationDbContext Db => _harness.Db;

    public PoolHarness Harness => _harness;

    public Account Account => _harness.Account;

    public Guid AccountId => _harness.Account.Id;

    public MutableClock Clock => _harness.Clock;

    public Guid PoolId => _harness.PoolId;

    public Guid OwnerId => _harness.OwnerId;

    public Guid AndreiId { get; private set; }

    public Guid BogdanId { get; private set; }

    /// <summary>The +42.00 catch-up mark written by <c>CreatePool</c>, dated at inception.</summary>
    public Guid? InceptionMarkTransactionId { get; private set; }

    /// <summary>The +83.68 mark written immediately before Bogdan's units were priced.</summary>
    public Guid? BogdanMarkTransactionId { get; private set; }

    /// <summary>The two distribution events written by the close, Andrei first.</summary>
    public IReadOnlyList<DistributionLine> CloseLines { get; private set; } = [];

    /// <summary>
    /// §6 up to and including the Sep-12 subscription: pool 3,175.68, units
    /// 3,053.538461538462, NAV 1.04, and an 83.68 mark row on the account.
    /// </summary>
    public static async Task<PoolWorkedExample> ThroughBogdanAsync(params Account[] extraAccounts)
    {
        var harness = PoolHarness.Create(
            now: Inception.ToDateTime(new TimeOnly(12, 0)),
            openingBalance: OpeningBalance,
            extraAccounts: extraAccounts);

        var example = new PoolWorkedExample(harness);

        CreatePoolResponse created = await harness.SeedPoolAsync(
            inceptionDate: Inception,
            poolValueAtInception: PoolValueAtInception);

        created.MarkDelta.Should().Be(InceptionMarkDelta);
        created.SeedUnits.Should().Be(SeedUnits);
        example.InceptionMarkTransactionId = created.MarkTransactionId;

        harness.Advance(2);
        example.AndreiId = await harness.AddParticipantAsync("Andrei");
        Result<RecordSubscriptionResponse> andrei =
            await harness.SubscribeAsync(example.AndreiId, PoolValueAtInception, AndreiCash);
        Succeeded(andrei);
        andrei.Value.MarkDelta.Should().Be(0m, "a zero delta is skipped, not an error");
        andrei.Value.Units.Should().Be(AndreiUnits);

        harness.Advance(4);
        example.BogdanId = await harness.AddParticipantAsync("Bogdan");
        Result<RecordSubscriptionResponse> bogdan =
            await harness.SubscribeAsync(example.BogdanId, BogdanPreMoney, BogdanCash);
        Succeeded(bogdan);
        bogdan.Value.MarkDelta.Should().Be(BogdanMarkDelta);
        bogdan.Value.Units.Should().Be(BogdanUnits);
        example.BogdanMarkTransactionId = bogdan.Value.MarkTransactionId;

        harness.Today.Should().Be(BogdanSubscribes);
        return example;
    }

    /// <summary>
    /// §6 up to and including the Sep-18 withdrawal: pool 2,775.68, units
    /// 2,668.923076923077, NAV 1.04, owner diluted 34.38% → 26.50%.
    /// </summary>
    public static async Task<PoolWorkedExample> ThroughWithdrawalAsync(params Account[] extraAccounts)
    {
        PoolWorkedExample example = await ThroughBogdanAsync(extraAccounts);
        PoolHarness harness = example._harness;

        harness.Advance(6);
        Result<RecordRedemptionResponse> withdrawal = await harness.RedeemAsync(
            harness.OwnerId,
            PoolValueAtInception + AndreiCash + BogdanMarkDelta + BogdanCash,
            WithdrawalCash);

        Succeeded(withdrawal);
        withdrawal.Value.Units.Should().Be(WithdrawnUnits);

        harness.Today.Should().Be(OwnerWithdraws);
        (await harness.DerivedBalanceAsync()).Should().Be(PoolValueAfterWithdrawal);

        return example;
    }

    /// <summary>
    /// …plus the month-end close. Units retire, 157.69 is recorded as OWED, and
    /// <b>no cash leg is written</b> — the account still holds 2,935.82.
    /// </summary>
    public static async Task<PoolWorkedExample> ThroughCloseAsync(params Account[] extraAccounts)
    {
        PoolWorkedExample example = await ThroughWithdrawalAsync(extraAccounts);

        example._harness.Advance(12);
        Result<CloseDistributionResponse> close = await example._harness.CloseAsync(ClosePoolValue);
        Succeeded(close);

        close.Value.MarkDelta.Should().Be(CloseMarkDelta);
        close.Value.NavPerUnit.Should().Be(NavAtClose);
        close.Value.TotalCash.Should().Be(TotalPayout);
        close.Value.Lines.Should().HaveCount(2, "the owner's profit stays in; only the friends are paid");

        example.CloseLines = close.Value.Lines;
        example._harness.Today.Should().Be(MonthCloses);

        return example;
    }

    /// <summary>
    /// …plus the payout actually going out, two days later. This is the leg §6
    /// dates at the close and the implementation deliberately does not.
    /// </summary>
    public static async Task<PoolWorkedExample> ThroughSettlementAsync(params Account[] extraAccounts)
    {
        PoolWorkedExample example = await ThroughCloseAsync(extraAccounts);
        await example.SettleEveryPayoutAsync();
        return example;
    }

    /// <summary>Advances to "Oct 2" and settles every line of the close.</summary>
    public async Task SettleEveryPayoutAsync()
    {
        _harness.Advance(2);

        foreach (DistributionLine line in CloseLines)
        {
            Result<SettleDistributionResponse> settled = await _harness.SettleAsync(line.EventId);
            Succeeded(settled);
        }

        _harness.Today.Should().Be(PayoutSettles);
    }

    /// <summary>The pool account's derived balance today — anchor plus every live movement.</summary>
    public Task<decimal> DerivedBalanceAsync() => _harness.DerivedBalanceAsync();

    private static void Succeeded<T>(Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
}
