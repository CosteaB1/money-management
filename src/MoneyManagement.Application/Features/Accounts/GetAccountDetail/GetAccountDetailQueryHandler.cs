using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Pools;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.GetAccountDetail;

/// <summary>
/// Returns per-account detail used by the frontend's `/accounts/{id}` page.
/// Decomposes account activity into contributions (inbound transfer legs),
/// withdrawals (outbound transfer legs), and net P&amp;L (balance adjustments
/// signed by direction). Mirrors <c>GetSummaryQueryHandler</c>'s row-date FX
/// convention — each row converts at its own <c>TransactionDate</c>.
/// <para>
/// <b>On a pooled account those three figures are the OWNER's, not the
/// account's.</b> Gross they would not be approximate, they would be actively
/// false: two friends' USD 2,000 of subscriptions would read as the user's
/// contributions, their payouts as the user's withdrawals, and the entire
/// pool's return as the user's profit. The account's BALANCE stays gross here
/// and on every other surface (see <see cref="AccountDetailDto"/>) — this card
/// and the two net-worth surfaces are the only places the owner's share is
/// applied.
/// </para>
/// <para>
/// <b>The two corrections are NOT the same operation.</b> That asymmetry is the
/// whole subtlety of this handler:
/// </para>
/// <list type="number">
/// <item>
/// <b>Transfer legs are 0% or 100% yours — included or excluded WHOLE, never
/// scaled.</b> A friend's subscription is entirely their money arriving; the
/// owner's own transfer out to another exchange is entirely theirs leaving.
/// Scaling either one would invent a contribution nobody made. So a leg drops
/// out exactly when a NON-OWNER participant's unit event links it as its
/// movement transaction, and is kept otherwise — owner legs and every non-pool
/// transfer included.
/// </item>
/// <item>
/// <b>Adjustment marks ARE scaled</b> — by the fraction in force at the INSTANT
/// each one was struck. A mark re-prices the whole pool, so the owner's part of
/// it is the share they held when it was written. Every pool command writes the
/// mark FIRST and the unit events it prices second, inside one
/// <c>SaveChangesAsync</c> (see <c>PoolMark</c>), so the applicable fraction is
/// the pre-money one.
/// </item>
/// </list>
/// <para>
/// <b>The instant, not the day.</b> <c>PoolUnitRegister.OwnershipCurve</c> emits
/// exactly one point per distinct effective date, carrying that day's END-OF-DAY
/// state, so it cannot on its own say where inside a day a mark sits. This
/// handler used to approximate that with <c>OwnedFractionAsOf(date − 1)</c>,
/// which is exact while a date holds at most one capital event — the normal case
/// and the one POOLED-CAPITAL.md §6 walks — and wrong the moment a capital event
/// and a LATER mark share a calendar date, because the mark then gets the
/// PRE-event fraction. Live defect, 2026-09-07: a pool bootstrapped, subscribed
/// and closed on one day credited the owner with the whole +208.00 close mark
/// instead of 1092/2092 of it, over-stating Net P&amp;L by 99.43 USD.
/// <c>PoolMarkAttribution</c> replaces the approximation with the real pairing —
/// each mark is attributed at the units state in force when it was written,
/// established from the recorded <c>PoolValuePreMoney</c> rather than from ids
/// that a same-millisecond save leaves unorderable. Its doc carries the
/// derivation and the two residual ambiguities.
/// </para>
/// <para>
/// Both readings still come off ONE fold, so the card cannot disagree with the
/// dashboard: on a day with no capital event the attributed fraction is the same
/// number <see cref="AccountOwnership.OwnedFractionAsOf"/> returns, and §6's
/// spread-across-days figures (+42.00, +43.68, +42.44) are untouched.
/// </para>
/// <para>
/// An account no ownership source mentions — every account until a pool exists
/// — gets a fraction of 1.0 on every date, an EMPTY attribution and nothing
/// excluded, so the output is bit-for-bit what it was before the seam existed.
/// </para>
/// </summary>
internal sealed class GetAccountDetailQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IEnumerable<IAccountOwnershipSource> ownershipSources,
    IDateTimeProvider clock)
    : IQueryHandler<GetAccountDetailQuery, AccountDetailDto>
{
    public async Task<Result<AccountDetailDto>> Handle(
        GetAccountDetailQuery query,
        CancellationToken cancellationToken)
    {
        // Archived accounts must remain reachable here so the user can drill
        // into a closed account's history — IgnoreQueryFilters bypasses the
        // global IsArchived filter on Account.
        Account? account = await db.Accounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == query.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure<AccountDetailDto>(AccountErrors.NotFound(query.Id));
        }

        // The global IsDeleted query filter excludes soft-deleted rows under EF
        // Core; the explicit predicate is defense-in-depth so unit tests (which
        // bypass model configuration) behave identically.
        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => t.AccountId == account.Id)
            .Select(t => new
            {
                t.Id,
                t.TransactionDate,
                t.Direction,
                t.IsTransfer,
                t.IsAdjustment,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        // Loaded exactly the way GetNetWorthQueryHandler loads it: one
        // round-trip per registered source, then sliced in memory per row.
        //
        // Loaded UNCONDITIONALLY rather than "only if this account is pooled" -
        // the seam is producer-agnostic on purpose, and gating it on the pools
        // table would bake pools into the one abstraction built not to know
        // about them. With no source registered the ledger answers 1.0 for
        // everything and the load costs nothing.
        AccountOwnershipLedger ownership =
            await AccountOwnershipLedger.LoadAsync(ownershipSources, cancellationToken);

        // Two different questions about pools, with two deliberately different
        // answers about ARCHIVED ones - see LoadPoolLedgerAsync.
        bool isPooled = await PooledAccountGuard.IsPooledAsync(db, account.Id, cancellationToken);

        PoolLedgerForAccount poolLedger = await LoadPoolLedgerAsync(db, account.Id, cancellationToken);

        // Bucketing rules (see WIKI "Known rough edges" for the canonical
        // rationale on the IsTransfer/IsAdjustment classifier):
        //   IsTransfer && Income  -> contribution (inbound transfer leg)
        //   IsTransfer && Expense -> withdrawal  (outbound transfer leg)
        //   IsAdjustment && Income  -> +P&L
        //   IsAdjustment && Expense -> -P&L
        //   else -> real activity (counted, not summed into any bucket)
        // is_transfer and is_adjustment are mutually exclusive at the domain
        // layer (TransactionErrors.TransferAndAdjustmentAreMutuallyExclusive),
        // so the branches never overlap.
        DateTime now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var yearStart = new DateOnly(today.Year, 1, 1);

        // RULE 2's input, resolved once for the whole account: which share of
        // each re-pricing mark was the owner's at the instant it was struck.
        // Empty on every account with no pool, which is the fallback below being
        // the pre-pools behaviour verbatim.
        //
        // NO EXTRA QUERY. The pool's ledger is already loaded above and the
        // balance seam is folded from `rows`, which this handler had to load
        // anyway - the same "opening anchor + Σ income − Σ expense" arithmetic
        // AccountBalanceLedger implements, over the one account in hand.
        IReadOnlyDictionary<Guid, decimal> markFractions = PoolMarkAttribution.Build(
            poolLedger.Events,
            poolLedger.OwnerParticipantIds,
            [.. rows
                .Where(r => r.IsAdjustment)
                .Select(r => new PoolMarkRow(
                    r.Id,
                    r.TransactionDate,
                    r.Direction == TransactionDirection.Income ? r.AmountValue : -r.AmountValue))],
            asOf =>
            {
                decimal balance = account.Balance.Amount;

                foreach (var row in rows)
                {
                    if (row.TransactionDate > asOf)
                    {
                        continue;
                    }

                    balance += row.Direction == TransactionDirection.Income
                        ? row.AmountValue
                        : -row.AmountValue;
                }

                return balance;
            },
            today);

        decimal incomeNative = 0m;
        decimal expenseNative = 0m;

        decimal allContrib = 0m, allWithdraw = 0m, allPnL = 0m;
        int allContribCount = 0, allWithdrawCount = 0, allAdjCount = 0;
        bool allMissing = false;

        decimal ytdContrib = 0m, ytdWithdraw = 0m, ytdPnL = 0m;
        int ytdContribCount = 0, ytdWithdrawCount = 0, ytdAdjCount = 0;
        bool ytdMissing = false;

        DateOnly? firstActivity = null;
        DateOnly? lastActivity = null;
        int realActivityCount = 0;

        foreach (var row in rows)
        {
            // Live balance arithmetic mirrors GetAccountsQueryHandler — every
            // non-deleted row moves the per-account balance, transfers and
            // adjustments included.
            if (row.Direction == TransactionDirection.Income)
            {
                incomeNative += row.AmountValue;
            }
            else
            {
                expenseNative += row.AmountValue;
            }

            if (firstActivity is null || row.TransactionDate < firstActivity)
            {
                firstActivity = row.TransactionDate;
            }

            if (lastActivity is null || row.TransactionDate > lastActivity)
            {
                lastActivity = row.TransactionDate;
            }

            bool inYtd = row.TransactionDate >= yearStart && row.TransactionDate <= today;

            if (!row.IsTransfer && !row.IsAdjustment)
            {
                realActivityCount++;
                continue;
            }

            // RULE 1 - whole legs, never scaled. This one is a non-owner
            // participant's money moving into or out of the pool, so it is 0%
            // the user's and leaves the card entirely: amount, COUNT and
            // missing-rate flag alike. Keeping the count would print "2
            // contributions" above a total holding only one of them; scaling
            // the amount would report a contribution the user never made. The
            // BALANCE above already took the row in, because the account really
            // does hold that money.
            if (row.IsTransfer && poolLedger.OutsideMovementLegs.Contains(row.Id))
            {
                continue;
            }

            decimal? mdl = await fxConverter.ConvertAsync(
                row.AmountValue,
                row.AmountCurrency,
                ReportingCurrencies.Mdl,
                row.TransactionDate,
                cancellationToken);

            if (row.IsTransfer)
            {
                if (row.Direction == TransactionDirection.Income)
                {
                    allContribCount++;
                    if (inYtd)
                    {
                        ytdContribCount++;
                    }

                    if (mdl is null)
                    {
                        allMissing = true;
                        if (inYtd)
                        {
                            ytdMissing = true;
                        }
                    }
                    else
                    {
                        allContrib += mdl.Value;
                        if (inYtd)
                        {
                            ytdContrib += mdl.Value;
                        }
                    }
                }
                else
                {
                    allWithdrawCount++;
                    if (inYtd)
                    {
                        ytdWithdrawCount++;
                    }

                    if (mdl is null)
                    {
                        allMissing = true;
                        if (inYtd)
                        {
                            ytdMissing = true;
                        }
                    }
                    else
                    {
                        allWithdraw += mdl.Value;
                        if (inYtd)
                        {
                            ytdWithdraw += mdl.Value;
                        }
                    }
                }
            }
            else
            {
                // IsAdjustment branch (mutual exclusivity guarantees this).
                allAdjCount++;
                if (inYtd)
                {
                    ytdAdjCount++;
                }

                if (mdl is null)
                {
                    allMissing = true;
                    if (inYtd)
                    {
                        ytdMissing = true;
                    }
                }
                else
                {
                    // RULE 2 - marks ARE scaled, by the share the user held at
                    // the INSTANT this mark was struck. On a pooled account
                    // PoolMarkAttribution has already paired every mark to the
                    // unit event it was written next to (or found that nothing
                    // was written after it), which is the only way to be right
                    // when a capital event and a mark land on the same calendar
                    // day.
                    //
                    // The fallback is the seam's own end-of-previous-day
                    // reading, and it is what every account without a pool takes:
                    // 1.0 everywhere, and byte-identical to the behaviour that
                    // shipped before pools existed. A registered-but-unrelated
                    // ownership source lands here too - the pool is the only
                    // producer that can speak to intra-day ordering, because it
                    // is the only one with a ledger.
                    decimal owned = markFractions.TryGetValue(row.Id, out decimal attributed)
                        ? attributed
                        : ownership.OwnedFractionAsOf(account.Id, PreMoney(row.TransactionDate));

                    decimal share = mdl.Value * owned;

                    decimal signed = row.Direction == TransactionDirection.Income
                        ? share
                        : -share;
                    allPnL += signed;
                    if (inYtd)
                    {
                        ytdPnL += signed;
                    }
                }
            }
        }

        decimal balance = account.Balance.Amount + incomeNative - expenseNative;

        decimal? balanceMdl = await fxConverter.ConvertAsync(
            balance,
            account.Balance.Currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        var allTime = new AccountActivityTotalsDto(
            allContrib,
            allWithdraw,
            allPnL,
            allContribCount,
            allWithdrawCount,
            allAdjCount,
            allMissing);

        var ytd = new AccountActivityTotalsDto(
            ytdContrib,
            ytdWithdraw,
            ytdPnL,
            ytdContribCount,
            ytdWithdrawCount,
            ytdAdjCount,
            ytdMissing);

        return Result.Success(new AccountDetailDto(
            account.Id,
            account.Name,
            account.Type,
            account.Balance.Currency,
            account.OpeningDate,
            account.IsArchived,
            isPooled,
            account.Notes,
            balance,
            balanceMdl,
            account.Balance.Amount,
            allTime,
            ytd,
            firstActivity,
            lastActivity,
            realActivityCount));
    }

    /// <summary>
    /// The pool sitting on <paramref name="accountId"/>, reduced to the two
    /// things this card needs: its unit ledger (RULE 2 - which share of each
    /// mark was the owner's) and the transaction ids that are the money leg of a
    /// NON-OWNER participant's unit event (RULE 1 - a friend's subscription
    /// arriving, their redemption or their monthly payout leaving). Whole legs,
    /// because a transfer leg belongs to exactly one participant.
    /// <para>
    /// ONE query, and empty after a single indexed lookup for an account with no
    /// pool - which is every account but one. The two facts are read from one
    /// join rather than two so they can never describe different sets of events.
    /// A unit event links the POOL-SIDE leg only
    /// (<c>RecordRedemptionCommandHandler</c> links <c>leg</c>, never
    /// <c>counterLeg</c>), so an owner redemption's landing leg on the
    /// destination account is untouched and still reads as a contribution over
    /// there. It is one: that really is the user's money arriving.
    /// </para>
    /// <para>
    /// <b><see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}(IQueryable{TEntity})"/>
    /// is mandatory</b>, for the same reason <c>PoolAccountOwnershipSource</c>
    /// needs it: <c>Pool</c> carries <c>HasQueryFilter(p =&gt; !p.IsArchived)</c>
    /// and this is an ALL-TIME figure. Archiving requires zero outside units, so
    /// the account is wholly the user's again TODAY - but the friends'
    /// subscriptions still happened, and letting the filter hide their legs would
    /// hand every one of them straight back to the owner's all-time
    /// contributions. That is the opposite of <c>isPooled</c>, which asks about
    /// today and therefore takes <c>PooledAccountGuard</c>'s non-archived reading.
    /// </para>
    /// </summary>
    private static async Task<PoolLedgerForAccount> LoadPoolLedgerAsync(
        IApplicationDbContext db,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        // Joined explicitly rather than through navigations because the pool
        // aggregates are configured with shadow FKs and no navigation
        // properties (see PoolUnitEventConfiguration). PoolParticipant carries
        // no query filter (archived participants must stay in every sum), so
        // the join reaches an exited friend's events too - which it must, since
        // this is an ALL-TIME figure.
        var rows = await db.Pools
            .IgnoreQueryFilters()
            .Where(p => p.AccountId == accountId)
            .Join(
                db.PoolUnitEvents,
                pool => pool.Id,
                unitEvent => unitEvent.PoolId,
                (pool, unitEvent) => unitEvent)
            .Join(
                db.PoolParticipants,
                unitEvent => unitEvent.ParticipantId,
                participant => participant.Id,
                (unitEvent, participant) => new { Event = unitEvent, participant.IsOwner })
            .ToListAsync(cancellationToken);

        var events = new List<PoolUnitEvent>(rows.Count);
        var ownerParticipantIds = new HashSet<Guid>();
        var outsideMovementLegs = new HashSet<Guid>();

        foreach (var row in rows)
        {
            events.Add(row.Event);

            if (row.IsOwner)
            {
                ownerParticipantIds.Add(row.Event.ParticipantId);
                continue;
            }

            if (row.Event.MovementTransactionId is Guid movementTransactionId)
            {
                outsideMovementLegs.Add(movementTransactionId);
            }
        }

        return new PoolLedgerForAccount(events, ownerParticipantIds, outsideMovementLegs);
    }

    /// <summary>
    /// The day before <paramref name="date"/> - the as-of that yields the
    /// ownership fraction in force BEFORE that date's unit events, since the
    /// curve carries one END-OF-DAY point per date.
    /// <para>
    /// Clamped at <see cref="DateOnly.MinValue"/>, where there is no day before:
    /// <c>AddDays(-1)</c> would throw, and a curve answers 1.0 for any date
    /// before its first point anyway, so the clamp changes no number.
    /// </para>
    /// </summary>
    private static DateOnly PreMoney(DateOnly date) =>
        date > DateOnly.MinValue ? date.AddDays(-1) : date;

    /// <summary>
    /// One account's pool, as this card reads it. All three collections are
    /// empty for an account with no pool, which is what makes the two rules
    /// no-ops there.
    /// </summary>
    /// <param name="Events">The pool's whole unit ledger, unordered.</param>
    /// <param name="OwnerParticipantIds">
    /// The participant rows flagged <c>IsOwner</c> — exactly one, by partial
    /// unique index.
    /// </param>
    /// <param name="OutsideMovementLegs">
    /// Transaction ids on this account that are a NON-OWNER participant's money
    /// leg.
    /// </param>
    private sealed record PoolLedgerForAccount(
        IReadOnlyList<PoolUnitEvent> Events,
        IReadOnlySet<Guid> OwnerParticipantIds,
        IReadOnlySet<Guid> OutsideMovementLegs);
}
