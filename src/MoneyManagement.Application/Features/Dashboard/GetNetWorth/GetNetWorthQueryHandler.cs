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
/// </summary>
internal sealed class GetNetWorthQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IExternalClaimSource claimSource,
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

        decimal grossAssetsMdl = 0m;
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
                accountsMissingFxRate++;
                continue;
            }

            grossAssetsMdl += converted.Value;
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
            claimsMissingFxRate));
    }
}
