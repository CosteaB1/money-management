using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Domain.Common;

namespace MoneyManagement.Infrastructure.FxRates;

/// <summary>
/// Wraps BNM's daily XML feed at <c>{BnmBaseUrl}?get_xml=1&amp;date=DD.MM.YYYY</c>.
/// Failure modes (404, 5xx, malformed XML, empty body, future date) all
/// collapse to "empty list" — the caller treats absence as "no data" rather
/// than an error.
/// </summary>
internal sealed class BnmRateProvider(
    HttpClient httpClient,
    IOptions<FxAutoFetchOptions> options,
    ILogger<BnmRateProvider> logger) : IBnmRateProvider
{
    private readonly string _baseUrl = options.Value.BnmBaseUrl;

    public async Task<IReadOnlyList<BnmRate>> GetRatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        // BNM expects DD.MM.YYYY — not the ISO format.
        string formatted = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        string url = $"{_baseUrl}?get_xml=1&date={formatted}";

        string content;
        try
        {
            HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "BNM returned {Status} for {Url}; treating as no rates.",
                    (int)response.StatusCode, url);
                return [];
            }

            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException without the
            // original token. Real cancellation falls through to the throw below.
            logger.LogWarning(ex, "BNM HTTP call timed out for {Url}; treating as no rates.", url);
            return [];
        }
        catch (OperationCanceledException)
        {
            // Bubble cancellation up — never swallow it as "no data".
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "BNM HTTP call failed for {Url}; treating as no rates.", url);
            return [];
        }

        return Parse(content);
    }

    /// <summary>
    /// Pure XML -&gt; <see cref="BnmRate"/> projection. Extracted so the
    /// Application test project can exercise it directly without spinning
    /// up an HttpClient (contract deviation called out in the BNM-auto-fetch
    /// slice's notes — there's no separate Infrastructure test project).
    /// </summary>
    public static IReadOnlyList<BnmRate> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        XElement? root = doc.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "ValCurs", StringComparison.Ordinal))
        {
            return [];
        }

        DateOnly asOf = ParseAsOf(root.Attribute("Date")?.Value);
        var results = new List<BnmRate>();

        foreach (XElement valute in root.Elements("Valute"))
        {
            string? charCode = valute.Element("CharCode")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(charCode) ||
                string.Equals(charCode, ReportingCurrencies.Mdl, StringComparison.Ordinal))
            {
                continue;
            }

            string? nominalText = valute.Element("Nominal")?.Value;
            string? valueText = valute.Element("Value")?.Value;
            if (string.IsNullOrWhiteSpace(valueText))
            {
                continue;
            }

            // Nominal defaults to 1 when missing (some legacy currencies omit it).
            decimal nominal = 1m;
            if (!string.IsNullOrWhiteSpace(nominalText) &&
                !decimal.TryParse(nominalText, NumberStyles.Number, CultureInfo.InvariantCulture, out nominal))
            {
                continue;
            }

            if (nominal <= 0m)
            {
                continue;
            }

            if (!decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ||
                value <= 0m)
            {
                continue;
            }

            decimal effective = value / nominal;
            results.Add(new BnmRate(charCode, effective, asOf));
        }

        return results;
    }

    private static DateOnly ParseAsOf(string? dateAttribute)
    {
        if (string.IsNullOrWhiteSpace(dateAttribute))
        {
            return default;
        }

        if (DateOnly.TryParseExact(dateAttribute, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            return parsed;
        }

        return default;
    }
}
