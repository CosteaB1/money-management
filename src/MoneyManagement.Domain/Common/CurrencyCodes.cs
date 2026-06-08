namespace MoneyManagement.Domain.Common;

/// <summary>
/// Helpers for working with 3-letter ISO 4217 currency codes.
/// Shared across <see cref="MoneyManagement.Domain.Accounts.Account"/> and
/// <see cref="MoneyManagement.Domain.FxRates.FxRate"/> so the validation rule
/// stays in one place.
/// </summary>
public static class CurrencyCodes
{
    public const int Length = 3;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="code"/> is exactly 3 uppercase
    /// ASCII letters (e.g. <c>MDL</c>, <c>USD</c>, <c>EUR</c>, <c>RON</c>).
    /// Matches the API-level regex <c>^[A-Z]{3}$</c>.
    /// </summary>
    public static bool IsValidIso(string? code)
    {
        if (code is null || code.Length != Length)
        {
            return false;
        }

        foreach (char c in code)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
