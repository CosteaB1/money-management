using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Application.Features.Accounts;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Dashboard.GetNetWorth;

/// <summary>
/// Computes today's net worth as gross assets minus external liabilities plus
/// external receivables.
/// <para>
/// Everything converts at TODAY's rate — this is the live card. The trend
/// handler deliberately converts at each point's own date instead; keep the two
/// explicit, because silently sharing one as-of between them is exactly the bug
/// class this slice exists to avoid.
/// </para>
/// <para>
/// Gross assets are the user's SHARE of each account, not its whole balance:
/// outside capital (see <see cref="IAccountOwnershipSource"/>) is split off
/// before anything else happens, so it never enters the identity and never has
/// to be subtracted back out. With no ownership source registered every fraction
/// is 1 and the arithmetic is bit-for-bit what it was.
/// </para>
/// </summary>
internal sealed class GetNetWorthQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IExternalClaimSource claimSource,
    IEnumerable<IAccountOwnershipSource> ownershipSources,
    IDateTimeProvider clock)
    : IQueryHandler<GetNetWorthQuery, NetWorthDto>
{
    public async Task<Result<NetWorthDto>> Handle(
        GetNetWorthQuery query,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Non-archived accounts only — archived accounts hide from the
        // dashboard (WIKI.md), same rule the trend handler applies.
        List<Account> accounts = await db.Accounts
            .Where(a => !a.IsArchived)
            .ToListAsync(cancellationToken);

        AccountBalanceLedger ledger = await AccountBalanceLedger.LoadAsync(db, cancellationToken);

        // Hoisted out of the loop for the same reason the balance ledger is:
        // one round-trip per source for the whole request, then in-memory slicing.
        AccountOwnershipLedger ownership =
            await AccountOwnershipLedger.LoadAsync(ownershipSources, cancellationToken);

        decimal grossAssetsMdl = 0m;
        decimal outsideCapitalMdl = 0m;
        int accountsMissingFxRate = 0;

        foreach (Account account in accounts)
        {
            decimal nativeBalance = ledger.NativeBalanceAsOf(account, today);

            decimal? converted = await fxConverter.ConvertAsync(
                nativeBalance,
                account.Balance.Currency,
                ReportingCurrencies.Mdl,
                today,
                cancellationToken);

            if (converted is null)
            {
                // Drops out of BOTH legs, not just gross: we know neither half of
                // an amount we can't value at all.
                accountsMissingFxRate++;
                continue;
            }

            // Split AFTER conversion. FX is a pure multiply, so owned + outside
            // still sums back to the converted whole, and it costs zero extra
            // rate lookups compared with converting each half separately.
            decimal owned = ownership.OwnedFractionAsOf(account.Id, today);
            grossAssetsMdl += converted.Value * owned;
            outsideCapitalMdl += converted.Value * (1m - owned);
        }

        IReadOnlyList<ExternalClaim> claims = await claimSource.GetHistoryAsync(cancellationToken);

        decimal liabilitiesMdl = 0m;
        decimal externalAssetsMdl = 0m;
        int claimsMissingFxRate = 0;

        foreach (ExternalClaim claim in claims)
        {
            decimal outstanding = claim.OutstandingAsOf(today);
            if (outstanding <= 0m)
            {
                // Settled (or over-settled) — no obligation left to report, and
                // no reason to demand an FX rate for it.
                continue;
            }

            decimal? converted = await fxConverter.ConvertAsync(
                outstanding,
                claim.Currency,
                ReportingCurrencies.Mdl,
                today,
                cancellationToken);

            if (converted is null)
            {
                claimsMissingFxRate++;
                continue;
            }

            if (claim.Side == ExternalClaimSide.ReducesNetWorth)
            {
                liabilitiesMdl += converted.Value;
            }
            else
            {
                externalAssetsMdl += converted.Value;
            }
        }

        return Result.Success(new NetWorthDto(
            grossAssetsMdl,
            liabilitiesMdl,
            externalAssetsMdl,
            grossAssetsMdl - liabilitiesMdl + externalAssetsMdl,
            accountsMissingFxRate,
            claimsMissingFxRate,
            outsideCapitalMdl));
    }
}
