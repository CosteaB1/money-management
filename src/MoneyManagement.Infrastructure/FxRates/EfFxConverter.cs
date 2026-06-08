using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Domain.FxRates;

namespace MoneyManagement.Infrastructure.FxRates;

/// <summary>
/// EF Core-backed <see cref="IFxConverter"/>. Looks up the latest direct
/// rate on or before <c>asOf</c>; falls back to the inverse pair; returns
/// <c>null</c> when neither exists.
/// </summary>
/// <remarks>
/// When multiple rates share the same <c>(from, to, asOf)</c> triple (e.g. a
/// user-entered <see cref="FxRateSource.Manual"/> row sits alongside a
/// scheduled <see cref="FxRateSource.BnmAuto"/> row), the converter prefers
/// the <see cref="FxRateSource.Manual"/> one. The tie-break uses an explicit
/// numeric key (<c>ThenBy(r =&gt; r.Source == FxRateSource.Manual ? 0 : 1)</c>),
/// which EF renders as a <c>CASE</c> expression in the <c>ORDER BY</c>. Do NOT
/// order by <c>r.Source</c> directly: <see cref="FxRate.Source"/> is mapped with
/// <c>HasConversion&lt;string&gt;()</c>, so EF would emit <c>ORDER BY source</c>
/// on the string column where <c>'BnmAuto' &lt; 'Manual'</c> lexicographically —
/// the opposite of the intended ordering.
/// </remarks>
internal sealed class EfFxConverter(IApplicationDbContext db) : IFxConverter
{
    public async Task<decimal?> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.Ordinal))
        {
            return amount;
        }

        FxRate? direct = await db.FxRates
            .AsNoTracking()
            .Where(r =>
                r.FromCurrency == fromCurrency &&
                r.ToCurrency == toCurrency &&
                r.AsOf <= asOf)
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);

        if (direct is not null)
        {
            return amount * direct.Rate;
        }

        FxRate? inverse = await db.FxRates
            .AsNoTracking()
            .Where(r =>
                r.FromCurrency == toCurrency &&
                r.ToCurrency == fromCurrency &&
                r.AsOf <= asOf)
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);

        if (inverse is not null && inverse.Rate > 0m)
        {
            return amount * (1m / inverse.Rate);
        }

        return null;
    }
}
