namespace MoneyManagement.Domain.Common;

/// <summary>
/// Currencies that the app reports aggregate values in. v1 is single-user
/// self-hosted in Moldova, so MDL is the only reporting currency.
/// Centralized here so it isn't magic-stringed across the codebase.
/// </summary>
public static class ReportingCurrencies
{
    /// <summary>Moldovan Leu — the app's reporting currency.</summary>
    public const string Mdl = "MDL";
}
