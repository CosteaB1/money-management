using FluentAssertions;
using MoneyManagement.Api.Features.Reports;

namespace MoneyManagement.Api.Tests.Features.Reports;

/// <summary>
/// Unit tests for the RFC 4180 quoting rules in <see cref="CsvWriter.EscapeField"/>.
/// Quoting fires only when a field contains the delimiter, a double-quote, or a
/// line break; embedded quotes are doubled. Leading/trailing spaces are
/// deliberately NOT quoted (the writer keeps output diff-friendly and most
/// fields are short alphanumerics).
/// </summary>
public sealed class CsvWriterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EscapeField_NullOrEmpty_ReturnsEmptyString(string? value)
    {
        CsvWriter.EscapeField(value).Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("12345")]
    [InlineData("no-special-chars_here")]
    public void EscapeField_NoSpecialChars_ReturnsValueUnchanged(string value)
    {
        CsvWriter.EscapeField(value).Should().Be(value);
    }

    [Fact]
    public void EscapeField_FieldWithComma_IsWrappedInQuotes()
    {
        CsvWriter.EscapeField("a,b").Should().Be("\"a,b\"");
    }

    [Fact]
    public void EscapeField_FieldWithEmbeddedQuote_DoublesTheQuoteAndWraps()
    {
        // RFC 4180: a literal quote inside a quoted field is escaped as "".
        CsvWriter.EscapeField("say \"hi\"").Should().Be("\"say \"\"hi\"\"\"");
    }

    [Fact]
    public void EscapeField_FieldWithNewline_IsWrappedInQuotes()
    {
        CsvWriter.EscapeField("line1\nline2").Should().Be("\"line1\nline2\"");
    }

    [Fact]
    public void EscapeField_FieldWithCarriageReturn_IsWrappedInQuotes()
    {
        CsvWriter.EscapeField("line1\rline2").Should().Be("\"line1\rline2\"");
    }

    [Fact]
    public void EscapeField_FieldWithCrLf_IsWrappedInQuotes()
    {
        CsvWriter.EscapeField("line1\r\nline2").Should().Be("\"line1\r\nline2\"");
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("  both  ")]
    public void EscapeField_LeadingOrTrailingSpaces_AreNotQuoted(string value)
    {
        // Documents current behavior: spaces alone do not trigger quoting.
        CsvWriter.EscapeField(value).Should().Be(value);
    }

    [Fact]
    public void EscapeField_OnlyEmbeddedQuotesNoOtherSpecials_StillQuotes()
    {
        CsvWriter.EscapeField("\"").Should().Be("\"\"\"\"");
    }

    [Fact]
    public void FormatDecimal_UsesInvariantCulture()
    {
        CsvWriter.FormatDecimal(1234.56m).Should().Be("1234.56");
    }

    [Fact]
    public void FormatDate_UsesIsoFormat()
    {
        CsvWriter.FormatDate(new DateOnly(2026, 6, 2)).Should().Be("2026-06-02");
    }

    [Fact]
    public void JoinRow_JoinsFieldsWithCommaDelimiter()
    {
        CsvWriter.JoinRow(["a", "b", "c"]).Should().Be("a,b,c");
    }
}
