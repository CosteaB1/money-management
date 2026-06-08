using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.FxRates.RefreshBnmRates;

/// <summary>
/// Fetches BNM rates and reconciles them against the FxRates table.
/// Priority rule: Manual rows are immutable from this code path. If a
/// Manual row exists for the same (from, MDL, asOf) triple, the BNM value
/// is skipped — even if the values differ.
/// </summary>
internal sealed class RefreshBnmRatesCommandHandler(
    IApplicationDbContext db,
    IBnmRateProvider provider,
    IDateTimeProvider clock,
    ILogger<RefreshBnmRatesCommandHandler> logger)
    : ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>
{
    public async Task<Result<RefreshBnmRatesResponse>> Handle(
        RefreshBnmRatesCommand command,
        CancellationToken cancellationToken)
    {
        DateOnly asOf = command.Date ?? DateOnly.FromDateTime(clock.UtcNow);

        // 1. Resolve the currency set we care about.
        HashSet<string> wanted;
        if (command.CurrencyFilter is { Count: > 0 } filter)
        {
            wanted = filter
                .Where(c => !string.Equals(c, ReportingCurrencies.Mdl, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
        }
        else
        {
            // Derive from active accounts — no point fetching JPY if the user
            // has never held a JPY account.
            List<string> heldCurrencies = await db.Accounts
                .AsNoTracking()
                .Select(a => a.Balance.Currency)
                .Distinct()
                .Where(c => c != ReportingCurrencies.Mdl)
                .ToListAsync(cancellationToken);

            wanted = heldCurrencies.ToHashSet(StringComparer.Ordinal);
        }

        if (wanted.Count == 0)
        {
            logger.LogInformation(
                "BNM refresh for {AsOf}: no non-MDL accounts, skipping provider call.", asOf);
            return new RefreshBnmRatesResponse(Fetched: 0, Inserted: 0, Updated: 0, Skipped: 0);
        }

        // 2. Fetch from BNM. Empty list = nothing published (weekend/holiday/network) — treat as no-op.
        IReadOnlyList<BnmRate> fetched = await provider.GetRatesAsync(asOf, cancellationToken);
        if (fetched.Count == 0)
        {
            logger.LogInformation("BNM refresh for {AsOf}: provider returned no rates.", asOf);
            return new RefreshBnmRatesResponse(Fetched: 0, Inserted: 0, Updated: 0, Skipped: 0);
        }

        // Pre-load existing rows for this asOf so we don't query inside the loop.
        // We key by (FromCurrency, Source). Both Manual and BnmAuto can co-exist
        // for the same triple after this slice ships.
        List<FxRate> existingForDate = await db.FxRates
            .Where(r =>
                r.ToCurrency == ReportingCurrencies.Mdl &&
                r.AsOf == asOf &&
                wanted.Contains(r.FromCurrency))
            .ToListAsync(cancellationToken);

        Dictionary<(string From, FxRateSource Source), FxRate> existingByKey =
            existingForDate.ToDictionary(r => (r.FromCurrency, r.Source));

        int inserted = 0;
        int updated = 0;
        int skipped = 0;
        bool dirty = false;

        foreach (BnmRate bnmRate in fetched)
        {
            if (!wanted.Contains(bnmRate.CharCode))
            {
                skipped++;
                continue;
            }

            DateOnly rowAsOf = bnmRate.AsOf == default ? asOf : bnmRate.AsOf;

            // Manual wins. Skip even if values differ.
            if (existingByKey.ContainsKey((bnmRate.CharCode, FxRateSource.Manual)))
            {
                skipped++;
                continue;
            }

            if (existingByKey.TryGetValue((bnmRate.CharCode, FxRateSource.BnmAuto), out FxRate? existing))
            {
                if (existing.Rate == bnmRate.Rate)
                {
                    skipped++;
                    continue;
                }

                Result update = existing.UpdateRate(bnmRate.Rate);
                if (update.IsFailure)
                {
                    logger.LogWarning(
                        "BNM refresh: refusing invalid rate for {Code}->MDL on {AsOf}: {Error}",
                        bnmRate.CharCode, rowAsOf, update.Error);
                    skipped++;
                    continue;
                }

                updated++;
                dirty = true;
                continue;
            }

            // No row yet — create a fresh BnmAuto.
            Result<FxRate> create = FxRate.Create(
                bnmRate.CharCode,
                ReportingCurrencies.Mdl,
                bnmRate.Rate,
                rowAsOf,
                FxRateSource.BnmAuto);

            if (create.IsFailure)
            {
                logger.LogWarning(
                    "BNM refresh: refusing invalid rate for {Code}->MDL on {AsOf}: {Error}",
                    bnmRate.CharCode, rowAsOf, create.Error);
                skipped++;
                continue;
            }

            db.FxRates.Add(create.Value);
            existingByKey[(bnmRate.CharCode, FxRateSource.BnmAuto)] = create.Value;
            inserted++;
            dirty = true;
        }

        if (dirty)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "BNM refresh for {AsOf}: fetched={Fetched}, inserted={Inserted}, updated={Updated}, skipped={Skipped}.",
            asOf, fetched.Count, inserted, updated, skipped);

        return new RefreshBnmRatesResponse(
            Fetched: fetched.Count,
            Inserted: inserted,
            Updated: updated,
            Skipped: skipped);
    }
}
