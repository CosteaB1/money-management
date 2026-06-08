namespace MoneyManagement.Application.Abstractions.FxRates;

/// <summary>
/// Fetches official MDL exchange rates from Banca Națională a Moldovei
/// (bnm.md). The implementation lives in Infrastructure and wraps the
/// <c>get_xml=1&amp;date=DD.MM.YYYY</c> endpoint.
/// </summary>
public interface IBnmRateProvider
{
    /// <summary>
    /// Returns BNM's official MDL rates for the given date. Returns an empty
    /// list if BNM has no data for that date (weekend, holiday, future date,
    /// network failure, malformed response). The caller treats "empty" as
    /// "nothing to update" — never as an error.
    /// </summary>
    Task<IReadOnlyList<BnmRate>> GetRatesAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// A single foreign-currency -&gt; MDL rate from BNM. <see cref="Rate"/> is
/// the effective per-unit value (BNM's <c>Value / Nominal</c>, so for JPY:
/// <c>11.3120 / 100 = 0.11312</c>).
/// </summary>
public sealed record BnmRate(string CharCode, decimal Rate, DateOnly AsOf);
