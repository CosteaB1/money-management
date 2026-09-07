using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Pools.GetPools;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.GetPoolDetail;

/// <summary>
/// The <c>/pools/{id}</c> drill-in: the pool folded today, its roster with
/// per-participant economics, the whole ledger newest-first, the two warnings
/// the frontend has to be able to raise (unpaid payouts, a stale mark) and the
/// reconciliation tripwire.
/// <para>
/// MDL conversion at TODAY's rate throughout, matching the list and
/// <c>AccountDto.BalanceMdl</c>: unconvertible amounts come back <c>null</c>
/// with <c>MissingFxRate</c> flipped, never as a silent zero.
/// </para>
/// </summary>
internal sealed class GetPoolDetailQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : IQueryHandler<GetPoolDetailQuery, PoolDetailDto>
{
    public async Task<Result<PoolDetailDto>> Handle(
        GetPoolDetailQuery query,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so an ARCHIVED pool stays drillable — the loan and
        // savings-goal detail convention. Archiving a pool requires zero outside
        // units, so what is left is a finished story the user should still be
        // able to read.
        Pool? pool = await db.Pools
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken);

        if (pool is null)
        {
            return Result.Failure<PoolDetailDto>(PoolErrors.NotFound(query.Id));
        }

        PoolReadContext context = await PoolReadContext.LoadAsync(db, [pool], cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);

        Account? account = context.AccountFor(pool);

        // Through PoolReadContext, never hand-rolled: the list handler folds the
        // same three lines and the whole point of the context is that the two
        // cannot drift apart.
        PoolSnapshot? snapshot = context.SnapshotFor(pool, today);

        if (account is null || snapshot is null)
        {
            // Unreachable through the app: the pool -> account FK is RESTRICT.
            // Both go null together - SnapshotFor needs the same account.
            return Result.Failure<PoolDetailDto>(AccountErrors.NotFound(pool.AccountId));
        }

        IReadOnlyList<PoolParticipant> participants = context.ParticipantsFor(pool.Id);
        IReadOnlyList<PoolUnitEvent> events = context.EventsFor(pool.Id);

        // Every row on the pool account, plus any transaction a unit event links
        // that somehow lives elsewhere. One query serves the reconciliation, the
        // mark-staleness warning and the per-event account labels.
        Guid[] linkedTransactionIds =
        [
            .. events.Where(e => e.MovementTransactionId is not null).Select(e => e.MovementTransactionId!.Value),
        ];

        // The global IsDeleted query filter excludes soft-deleted rows under EF
        // Core; the explicit predicate is defense-in-depth for unit tests, which
        // bypass model configuration.
        var transactionRows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => t.AccountId == pool.AccountId || linkedTransactionIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.AccountId,
                t.TransactionDate,
                t.Description,
                t.Direction,
                t.Amount.Amount,
                t.Amount.Currency,
                t.IsTransfer,
                t.IsAdjustment,
            })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> accountNameById = await ResolveAccountNamesAsync(
            db,
            account,
            [.. transactionRows.Select(t => t.AccountId).Distinct()],
            cancellationToken);

        var accountIdByTransactionId = transactionRows.ToDictionary(t => t.Id, t => t.AccountId);

        PoolAccountRow[] accountRows =
        [
            .. transactionRows
                .Where(t => t.AccountId == pool.AccountId)
                .Select(t => new PoolAccountRow(
                    t.Id,
                    t.TransactionDate,
                    t.Description,
                    t.Direction,
                    t.Amount,
                    t.Currency,
                    t.IsTransfer,
                    t.IsAdjustment)),
        ];

        // MARK STALENESS. The mark is the IsAdjustment row that brings the
        // account from what the app believes to what the exchange actually
        // holds, and every NAV is struck against it — a stale mark silently
        // transfers value between the user and the friends, permanently, and is
        // the main way this model goes quietly wrong.
        //
        // What is measured is the last time the value was CONFIRMED, not the
        // last adjustment ROW. PoolMark writes no row when the typed value
        // already equals the derived one (a zero delta is skipped, not an
        // error) - the normal case for a pool parked in stablecoins and priced
        // on schedule. Counting rows alone would march that pool past 30/60/90
        // days of invented staleness on the one tripwire the design says the
        // user has to trust. Every non-Seed unit event made the user type the
        // pre-money total, so its date confirms the value just as hard.
        //
        // Adjustments are scoped to the pool's own life as well: the account can
        // carry snapshots from before inception (the real Binanance account has
        // them back to 2026-08-27) that say nothing about this pool.
        DateOnly? lastAdjustment = accountRows
            .Where(r => r.IsAdjustment && r.Date >= pool.InceptionDate && r.Date <= today)
            .Select(r => (DateOnly?)r.Date)
            .DefaultIfEmpty(null)
            .Max();

        // No extra query: the ledger is already in scope.
        DateOnly? lastPricedEvent = events
            .Where(e => e.Kind != PoolUnitEventKind.Seed && e.OccurredOn <= today)
            .Select(e => (DateOnly?)e.OccurredOn)
            .DefaultIfEmpty(null)
            .Max();

        DateOnly? lastMarkDate = lastAdjustment is DateOnly adjusted && lastPricedEvent is DateOnly priced
            ? (adjusted > priced ? adjusted : priced)
            : lastAdjustment ?? lastPricedEvent;

        int? markAgeDays = lastMarkDate is DateOnly marked
            ? today.DayNumber - marked.DayNumber
            : null;

        decimal outsideCapital = GetPoolsQueryHandler.OutsideCapital(snapshot);
        decimal ownerFraction = GetPoolsQueryHandler.OwnerFractionOf(snapshot);

        decimal? poolValueMdl = await fxConverter.ConvertAsync(
            snapshot.PoolValue,
            pool.Currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        decimal? outsideCapitalMdl = await fxConverter.ConvertAsync(
            outsideCapital,
            pool.Currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        bool missingFxRate = poolValueMdl is null || outsideCapitalMdl is null;

        (decimal unpaidCash, int unpaidCount) = GetPoolsQueryHandler.UnpaidDistributions(events, today);

        var participantsById = participants.ToDictionary(p => p.Id);

        var participantDtos = new List<PoolParticipantDto>(snapshot.Positions.Count);
        foreach (PoolPosition position in snapshot.Positions)
        {
            decimal? stakeMdl = position.Stake is decimal stake
                ? await fxConverter.ConvertAsync(
                    stake,
                    pool.Currency,
                    ReportingCurrencies.Mdl,
                    today,
                    cancellationToken)
                : null;

            missingFxRate |= position.Stake is not null && stakeMdl is null;

            (decimal ownedCash, int ownedCount) = GetPoolsQueryHandler.UnpaidDistributions(
                [.. events.Where(e => e.ParticipantId == position.ParticipantId)],
                today);

            participantsById.TryGetValue(position.ParticipantId, out PoolParticipant? participant);

            participantDtos.Add(new PoolParticipantDto(
                position.ParticipantId,
                position.Name,
                position.IsOwner,
                position.IsArchived,
                participant?.JoinedOn ?? pool.InceptionDate,
                position.Units,
                // Unit counts only — NAV never enters the fraction, so a NAV
                // later found to be wrong misprices nothing retroactively.
                OwnershipPercent: Math.Round(position.OwnedFraction * 100m, 6, MidpointRounding.AwayFromZero),
                position.Stake,
                stakeMdl,
                position.CapitalBase,
                position.Distributable,
                ownedCash,
                ownedCount,
                MissingFxRate: position.Stake is not null && stakeMdl is null));
        }

        // Newest-first; the id (UUIDv7) breaks same-day ties so the most
        // recently recorded event leads — GetLoanDetail's convention, with the
        // time-ordered id standing in for CreatedAt.
        var eventDtos = new List<PoolUnitEventDto>(events.Count);
        foreach (PoolUnitEvent unitEvent in events.OrderByDescending(e => e.OccurredOn).ThenByDescending(e => e.Id))
        {
            Guid? movementAccountId = null;
            string? movementAccountName = null;

            if (unitEvent.MovementTransactionId is Guid transactionId
                && accountIdByTransactionId.TryGetValue(transactionId, out Guid resolvedAccountId))
            {
                movementAccountId = resolvedAccountId;
                movementAccountName = accountNameById.GetValueOrDefault(resolvedAccountId);
            }

            eventDtos.Add(new PoolUnitEventDto(
                unitEvent.Id,
                unitEvent.ParticipantId,
                participantsById.GetValueOrDefault(unitEvent.ParticipantId)?.Name ?? string.Empty,
                unitEvent.Kind,
                unitEvent.OccurredOn,
                unitEvent.Units,
                unitEvent.UnitsDelta,
                unitEvent.NavPerUnit,
                unitEvent.PoolValuePreMoney,
                unitEvent.Cash?.Amount,
                unitEvent.Cash?.Currency,
                unitEvent.SettledOn,
                IsUnpaid: unitEvent.Kind == PoolUnitEventKind.Distribution && unitEvent.SettledOn is null,
                unitEvent.MovementTransactionId,
                movementAccountId,
                movementAccountName,
                unitEvent.Notes));
        }

        PoolReconciliationDto reconciliation = PoolReconciliation.Build(
            pool,
            snapshot,
            events,
            accountRows,
            asOf => context.BalanceAsOf(account, asOf),
            today);

        return Result.Success(new PoolDetailDto(
            pool.Id,
            pool.AccountId,
            account.Name,
            account.Balance.Currency,
            account.IsArchived,
            pool.Name,
            pool.Currency,
            pool.InceptionDate,
            pool.Notes,
            pool.IsArchived,
            CreatedOn: pool.CreatedAt,
            AsOf: today,
            snapshot.AccountBalance,
            unpaidCash,
            unpaidCount,
            snapshot.PoolValue,
            poolValueMdl,
            snapshot.TotalUnits,
            snapshot.NavPerUnit,
            OwnerParticipantId: snapshot.Positions.FirstOrDefault(p => p.IsOwner)?.ParticipantId,
            ownerFraction,
            outsideCapital,
            outsideCapitalMdl,
            missingFxRate,
            lastMarkDate,
            markAgeDays,
            participantDtos,
            eventDtos,
            reconciliation));
    }

    /// <summary>
    /// Names for every account a movement touches. The pool's own account is
    /// already loaded; anything else (a redemption's destination) is fetched in
    /// one extra round-trip, and only when there is one.
    /// <para>
    /// <c>IgnoreQueryFilters</c> with no archive predicate: an archived account
    /// still labels a historical movement.
    /// </para>
    /// </summary>
    private static async Task<Dictionary<Guid, string>> ResolveAccountNamesAsync(
        IApplicationDbContext db,
        Account poolAccount,
        IReadOnlyCollection<Guid> referencedAccountIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string> { [poolAccount.Id] = poolAccount.Name };

        Guid[] others = [.. referencedAccountIds.Where(id => id != poolAccount.Id)];
        if (others.Length == 0)
        {
            return names;
        }

        var rows = await db.Accounts
            .IgnoreQueryFilters()
            .Where(a => others.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            names[row.Id] = row.Name;
        }

        return names;
    }
}
