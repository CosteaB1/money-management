namespace MoneyManagement.Infrastructure.FxRates;

/// <summary>
/// Bound from the <c>Fx:AutoFetch</c> section of <c>appsettings.json</c>.
/// Defaults are safe to ship: enabled, 30-day backfill, daily refresh, and
/// the live BNM URL. Tests disable the feature wholesale by setting
/// <see cref="Enabled"/> to <c>false</c>.
/// </summary>
public sealed class FxAutoFetchOptions
{
    /// <summary>Master switch. When <c>false</c>, the hosted service exits immediately.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of days to backfill on startup (counting today). 30 covers the
    /// "missing-rate" problem for any transaction in the trailing month —
    /// older history relies on the asOf-floor in <c>IFxConverter</c>.
    /// </summary>
    public int BackfillDays { get; set; } = 30;

    /// <summary>How often the service re-fetches today's rate after the initial backfill.</summary>
    public int RefreshIntervalHours { get; set; } = 24;

    /// <summary>Override the live URL for tests / staging mirrors.</summary>
    public string BnmBaseUrl { get; set; } = "https://www.bnm.md/en/official_exchange_rates";
}
