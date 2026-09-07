using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Pools.GetPools;

/// <summary>
/// The <c>/pools</c> list. Every pool is folded through
/// <see cref="PoolUnitRegister"/> against its account's derived balance today,
/// so the NAV printed here is the NAV the next write command would strike.
/// <para>
/// MDL conversion happens at TODAY's rate — this is a live list, the same
/// convention as <c>AccountDto.BalanceMdl</c> and the net-worth card. The trend
/// handler's per-point dates are deliberately NOT shared with it.
/// </para>
/// </summary>
internal sealed class GetPoolsQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : IQueryHandler<GetPoolsQuery, IReadOnlyList<PoolDto>>
{
    public async Task<Result<IReadOnlyList<PoolDto>>> Handle(
        GetPoolsQuery query,
        CancellationToken cancellationToken)
    {
        // The is_archived = false global query filter excludes archived pools
        // under EF Core; the explicit predicate is defense-in-depth so unit
        // tests (which bypass model configuration) exercise the same rule.
        // Mirrors GetLoansQueryHandler's IncludeArchived switch exactly.
        IQueryable<Pool> poolsQuery = query.IncludeArchived
            ? db.Pools.IgnoreQueryFilters()
            : db.Pools.Where(p => !p.IsArchived);

        List<Pool> pools = await poolsQuery
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        if (pools.Count == 0)
        {
            return Result.Success<IReadOnlyList<PoolDto>>([]);
        }

        PoolReadContext context = await PoolReadContext.LoadAsync(db, pools, cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var dtos = new List<PoolDto>(pools.Count);

        foreach (Pool pool in pools)
        {
            Account? account = context.AccountFor(pool);

            // Through PoolReadContext, never hand-rolled: the detail handler folds
            // the same three lines and the whole point of the context is that the
            // two cannot drift apart.
            PoolSnapshot? snapshot = context.SnapshotFor(pool, today);

            if (account is null || snapshot is null)
            {
                // Unreachable through the app: the pool -> account FK is
                // RESTRICT. A pool whose account was removed out of band has no
                // value to report, so it is skipped rather than shown at zero.
                // Both go null together - SnapshotFor needs the same account.
                continue;
            }

            decimal outsideCapital = OutsideCapital(snapshot);
            decimal ownerFraction = OwnerFractionOf(snapshot);

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

            (decimal unpaidCash, int unpaidCount) = UnpaidDistributions(context.EventsFor(pool.Id), today);

            dtos.Add(new PoolDto(
                pool.Id,
                pool.AccountId,
                account.Name,
                pool.Name,
                pool.Currency,
                pool.InceptionDate,
                pool.Notes,
                pool.IsArchived,
                snapshot.AccountBalance,
                unpaidCash,
                unpaidCount,
                snapshot.PoolValue,
                poolValueMdl,
                snapshot.TotalUnits,
                snapshot.NavPerUnit,
                ParticipantCount: snapshot.Positions.Count(p => !p.IsArchived),
                ownerFraction,
                outsideCapital,
                outsideCapitalMdl,
                // Never a silent zero: an unconvertible amount reports null and
                // trips the flag, exactly like AccountDto.BalanceMdl.
                MissingFxRate: poolValueMdl is null || outsideCapitalMdl is null));
        }

        return Result.Success<IReadOnlyList<PoolDto>>(dtos);
    }

    /// <summary>
    /// Σ non-owner stakes. Archived participants are INCLUDED — an exited
    /// friend with residual units still holds other people's money.
    /// </summary>
    internal static decimal OutsideCapital(PoolSnapshot snapshot)
    {
        decimal total = 0m;
        foreach (PoolPosition position in snapshot.Positions)
        {
            if (!position.IsOwner && position.Stake is decimal stake)
            {
                total += stake;
            }
        }

        return total;
    }

    /// <summary>
    /// The user's share of the account, from unit counts only — the same
    /// arithmetic <see cref="PoolAccountOwnershipSource"/> hands the net-worth
    /// seam, routed through the same helper so the pool page and the dashboard
    /// cannot disagree.
    /// </summary>
    internal static decimal OwnerFractionOf(PoolSnapshot snapshot)
    {
        decimal ownerUnits = 0m;
        foreach (PoolPosition position in snapshot.Positions)
        {
            if (position.IsOwner)
            {
                ownerUnits += position.Units;
            }
        }

        return PoolUnitRegister.OwnedFractionOf(ownerUnits, snapshot.TotalUnits);
    }

    /// <summary>
    /// Distributions closed on or before <paramref name="asOf"/> whose cash has
    /// not left the account yet — the close/pay gap the frontend needs a button
    /// for.
    /// </summary>
    internal static (decimal Cash, int Count) UnpaidDistributions(
        IReadOnlyList<PoolUnitEvent> events,
        DateOnly asOf)
    {
        decimal cash = 0m;
        int count = 0;

        foreach (PoolUnitEvent unitEvent in events)
        {
            if (unitEvent.Kind != PoolUnitEventKind.Distribution || unitEvent.OccurredOn > asOf)
            {
                continue;
            }

            if (unitEvent.SettledOn is DateOnly settled && settled <= asOf)
            {
                continue;
            }

            cash += unitEvent.Cash?.Amount ?? 0m;
            count++;
        }

        return (cash, count);
    }
}
