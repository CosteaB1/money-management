using MoneyManagement.Application.Abstractions.Imports;

namespace MoneyManagement.Infrastructure.Imports;

/// <summary>
/// Heuristic transfer detector backed by a hand-curated pattern table. Mirrors
/// the style of <see cref="CategorySuggester"/> — case-insensitive substring
/// match, evaluated against an explicit exclusion list first so payment-like
/// descriptions ("Achitare", "Plată") don't get flagged when they happen to
/// contain a transfer-shaped token.
/// </summary>
internal sealed class SubstringTransferDetector : ITransferDetector
{
    // Tokens that strongly indicate an internal transfer. NOTE: the generic
    // "TRANSFER" token was intentionally removed — maib stamps "Transfer" on
    // salary, ordinary payments, and real A2A moves alike, so keying on it
    // mis-flagged income/payments (e.g. "Transfer Salariul pentru iunie"). The
    // remaining tokens are unambiguous internal-movement signals.
    private static readonly string[] InclusionTokens =
    [
        "A2A",
        "RETRAGERE",
        "ATM",
    ];

    // Tokens that mark a regular payment even when an inclusion token is present.
    // E.g. "Achitare A2A" is still a goods-payment, not a card-to-card transfer.
    private static readonly string[] ExclusionTokens =
    [
        "ACHITARE",
        "PLATA",
        "PLATĂ",
        "SALARIU",
        "MIA",
        "CASHBACK",
    ];

    private static readonly char[] TokenSeparators =
    [
        ' ', '\t', '\r', '\n',
        ',', '.', ';', ':',
        '/', '\\', '|',
        '(', ')', '[', ']', '{', '}',
        '-', '_', '"', '\'',
    ];

    public bool IsLikelyTransfer(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        string normalized = description.ToUpperInvariant();
        string[] tokens = normalized.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        // Prefix match (not exact equality) so Romanian inflected forms are
        // still excluded — e.g. the token "SALARIUL" / "SALARIULUI" starts with
        // the "SALARIU" exclusion token. Exact equality missed those.
        foreach (string excluded in ExclusionTokens)
        {
            foreach (string token in tokens)
            {
                if (token.StartsWith(excluded, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        foreach (string included in InclusionTokens)
        {
            foreach (string token in tokens)
            {
                if (string.Equals(token, included, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
