using FluentAssertions;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Infrastructure.FxRates;

namespace MoneyManagement.Application.Tests.FxRates;

/// <summary>
/// Direct tests of <see cref="BnmRateProvider.Parse(string)"/>. Lives in the
/// Application test project because there's no Infrastructure test project
/// per BACKEND.md — a contract deviation called out in the BNM auto-fetch
/// slice's contract notes. The parsing logic is trivial enough that the
/// extra layer wasn't worth a third project.
/// </summary>
public class BnmRateProviderParseTests
{
    [Fact]
    public void Parse_WithNominalOne_ReturnsRateUnchanged()
    {
        const string xml = """
            <ValCurs Date="22.05.2026">
              <Valute>
                <CharCode>USD</CharCode>
                <Nominal>1</Nominal>
                <Value>17.5234</Value>
              </Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().ContainSingle();
        BnmRate usd = result[0];
        usd.CharCode.Should().Be("USD");
        usd.Rate.Should().Be(17.5234m);
        usd.AsOf.Should().Be(new DateOnly(2026, 5, 22));
    }

    [Fact]
    public void Parse_WithNominal100_DividesValueByNominal()
    {
        const string xml = """
            <ValCurs Date="22.05.2026">
              <Valute>
                <CharCode>JPY</CharCode>
                <Nominal>100</Nominal>
                <Value>11.3120</Value>
              </Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().ContainSingle();
        result[0].CharCode.Should().Be("JPY");
        result[0].Rate.Should().Be(0.113120m);
    }

    [Fact]
    public void Parse_WithMissingNominal_DefaultsToOne()
    {
        const string xml = """
            <ValCurs Date="22.05.2026">
              <Valute>
                <CharCode>USD</CharCode>
                <Value>17.50</Value>
              </Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().ContainSingle();
        result[0].Rate.Should().Be(17.50m);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<broken><missing-close>")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithMalformedOrEmptyXml_ReturnsEmptyList(string xml)
    {
        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WithEmptyValCurs_ReturnsEmptyList()
    {
        const string xml = "<ValCurs Date=\"22.05.2026\" />";

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WithWrongRootElement_ReturnsEmptyList()
    {
        const string xml = "<NotValCurs><Valute><CharCode>USD</CharCode><Value>17</Value></Valute></NotValCurs>";

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SkipsMdlRowsDefensively()
    {
        const string xml = """
            <ValCurs Date="22.05.2026">
              <Valute>
                <CharCode>MDL</CharCode>
                <Nominal>1</Nominal>
                <Value>1.0000</Value>
              </Valute>
              <Valute>
                <CharCode>USD</CharCode>
                <Nominal>1</Nominal>
                <Value>17.50</Value>
              </Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().ContainSingle();
        result[0].CharCode.Should().Be("USD");
    }

    [Fact]
    public void Parse_SkipsRowsWithNonPositiveValueOrNominal()
    {
        const string xml = """
            <ValCurs Date="22.05.2026">
              <Valute>
                <CharCode>BAD</CharCode>
                <Nominal>0</Nominal>
                <Value>1</Value>
              </Valute>
              <Valute>
                <CharCode>ZERO</CharCode>
                <Nominal>1</Nominal>
                <Value>0</Value>
              </Valute>
              <Valute>
                <CharCode>NEG</CharCode>
                <Nominal>1</Nominal>
                <Value>-5</Value>
              </Valute>
              <Valute>
                <CharCode>USD</CharCode>
                <Nominal>1</Nominal>
                <Value>17.50</Value>
              </Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);

        result.Should().ContainSingle();
        result[0].CharCode.Should().Be("USD");
    }

    [Fact]
    public void Parse_UsesInvariantCultureForDecimalSeparator()
    {
        // BNM uses dot as decimal separator regardless of locale. The parser
        // must not switch behaviour based on the current thread culture.
        const string xml = """
            <ValCurs Date="22.05.2026">
              <Valute>
                <CharCode>USD</CharCode>
                <Nominal>1</Nominal>
                <Value>17.5234</Value>
              </Valute>
            </ValCurs>
            """;

        System.Globalization.CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("ro-RO"); // uses comma as decimal sep
            IReadOnlyList<BnmRate> result = BnmRateProvider.Parse(xml);
            result.Should().ContainSingle();
            result[0].Rate.Should().Be(17.5234m);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
