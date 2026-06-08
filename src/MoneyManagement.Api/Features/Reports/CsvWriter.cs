using System.Globalization;
using System.Text;

namespace MoneyManagement.Api.Features.Reports;

/// <summary>
/// Minimal RFC 4180 CSV writer. Quoting only fires when a field contains a
/// delimiter, a quote, or a newline — keeps the output diff-friendly for
/// the common case (most descriptions are short and alphanumeric).
/// </summary>
internal static class CsvWriter
{
    private const char Delimiter = ',';
    private const char Quote = '"';

    public static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool mustQuote =
            value.IndexOf(Delimiter) >= 0 ||
            value.IndexOf(Quote) >= 0 ||
            value.IndexOf('\n') >= 0 ||
            value.IndexOf('\r') >= 0;

        if (!mustQuote)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append(Quote);
        foreach (char c in value)
        {
            if (c == Quote)
            {
                sb.Append(Quote);
            }
            sb.Append(c);
        }
        sb.Append(Quote);
        return sb.ToString();
    }

    public static string FormatDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string JoinRow(IEnumerable<string> fields) =>
        string.Join(Delimiter, fields);
}
