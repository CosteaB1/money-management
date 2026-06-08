using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MoneyManagement.Application.Features.Imports;

internal static partial class DuplicateSignature
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Compute(Guid accountId, DateOnly transactionDate, decimal amountMdl, string description)
    {
        string key = string.Create(CultureInfo.InvariantCulture,
            $"{accountId:N}|{transactionDate:yyyy-MM-dd}|{amountMdl:0.##}|{NormalizeDescription(description)}");

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash);
    }

    public static string NormalizeDescription(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return string.Empty;
        }

        string collapsed = WhitespaceRegex().Replace(description.Trim(), " ");
        return collapsed.ToLowerInvariant();
    }
}
