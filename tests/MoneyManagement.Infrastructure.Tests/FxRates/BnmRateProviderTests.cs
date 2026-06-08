using FluentAssertions;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Infrastructure.FxRates;

namespace MoneyManagement.Infrastructure.Tests.FxRates;

/// <summary>
/// <see cref="BnmRateProvider.Parse(string)"/> is a pure XML -&gt; rate
/// projection — the per-unit rate is <c>Value / Nominal</c>, MDL is filtered,
/// and every malformed/empty input collapses to an empty list (the caller
/// treats "no rates" as a non-error). No HttpClient or DB needed.
/// </summary>
public sealed class BnmRateProviderTests
{
    private const string WellFormed =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ValCurs Date="02.06.2026" name="Cursul oficial">
          <Valute ID="47">
            <NumCode>840</NumCode>
            <CharCode>USD</CharCode>
            <Nominal>1</Nominal>
            <Name>Dolar SUA</Name>
            <Value>17.5000</Value>
          </Valute>
          <Valute ID="84">
            <NumCode>978</NumCode>
            <CharCode>EUR</CharCode>
            <Nominal>1</Nominal>
            <Name>Euro</Name>
            <Value>19.2500</Value>
          </Valute>
          <Valute ID="36">
            <NumCode>392</NumCode>
            <CharCode>JPY</CharCode>
            <Nominal>100</Nominal>
            <Name>Yeni japonezi</Name>
            <Value>11.3100</Value>
          </Valute>
          <Valute ID="99">
            <NumCode>498</NumCode>
            <CharCode>MDL</CharCode>
            <Nominal>1</Nominal>
            <Name>Leu moldovenesc</Name>
            <Value>1.0000</Value>
          </Valute>
        </ValCurs>
        """;

    [Fact]
    public void Parse_WellFormed_ReturnsPerUnitRates()
    {
        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(WellFormed);

        // USD/EUR map straight through; JPY is per-100 so the per-unit rate is
        // 11.31 / 100 = 0.1131. MDL is filtered out → 3 rates.
        rates.Should().HaveCount(3);
        rates.Should().ContainSingle(r => r.CharCode == "USD" && r.Rate == 17.5000m);
        rates.Should().ContainSingle(r => r.CharCode == "EUR" && r.Rate == 19.2500m);
        rates.Should().ContainSingle(r => r.CharCode == "JPY" && r.Rate == 0.1131m);
    }

    [Fact]
    public void Parse_WellFormed_SetsAsOfFromDateAttribute()
    {
        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(WellFormed);

        rates.Should().AllSatisfy(r => r.AsOf.Should().Be(new DateOnly(2026, 6, 2)));
    }

    [Fact]
    public void Parse_FiltersOutMdl()
    {
        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(WellFormed);

        rates.Should().NotContain(r => r.CharCode == "MDL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_EmptyOrWhitespace_ReturnsEmpty(string? xml)
    {
        BnmRateProvider.Parse(xml!).Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmpty_NoThrow()
    {
        Action act = () => BnmRateProvider.Parse("<ValCurs><Valute><CharCode>USD");

        act.Should().NotThrow();
        BnmRateProvider.Parse("<ValCurs><Valute><CharCode>USD").Should().BeEmpty();
    }

    [Fact]
    public void Parse_WrongRootElement_ReturnsEmpty()
    {
        // A weekend/holiday/error doc with a different root is treated as "no data".
        BnmRateProvider.Parse("<html><body>No rates today</body></html>").Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyValCurs_ReturnsEmpty()
    {
        BnmRateProvider.Parse("""<ValCurs Date="02.06.2026"></ValCurs>""").Should().BeEmpty();
    }

    [Fact]
    public void Parse_MissingNominal_DefaultsToOne()
    {
        const string xml =
            """
            <ValCurs Date="02.06.2026">
              <Valute><CharCode>GBP</CharCode><Value>22.4000</Value></Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(xml);

        rates.Should().ContainSingle(r => r.CharCode == "GBP" && r.Rate == 22.4000m);
    }

    [Fact]
    public void Parse_MissingValue_SkipsValute()
    {
        const string xml =
            """
            <ValCurs Date="02.06.2026">
              <Valute><CharCode>USD</CharCode><Nominal>1</Nominal></Valute>
              <Valute><CharCode>EUR</CharCode><Nominal>1</Nominal><Value>19.25</Value></Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(xml);

        rates.Should().ContainSingle();
        rates.Single().CharCode.Should().Be("EUR");
    }

    [Fact]
    public void Parse_NonPositiveValueOrNominal_SkipsValute()
    {
        const string xml =
            """
            <ValCurs Date="02.06.2026">
              <Valute><CharCode>AAA</CharCode><Nominal>0</Nominal><Value>5.00</Value></Valute>
              <Valute><CharCode>BBB</CharCode><Nominal>1</Nominal><Value>0.00</Value></Valute>
              <Valute><CharCode>CCC</CharCode><Nominal>1</Nominal><Value>-3.00</Value></Valute>
              <Valute><CharCode>USD</CharCode><Nominal>1</Nominal><Value>17.50</Value></Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(xml);

        rates.Should().ContainSingle();
        rates.Single().CharCode.Should().Be("USD");
    }

    [Fact]
    public void Parse_UnparseableNominal_SkipsValute()
    {
        // A present-but-non-numeric Nominal fails decimal.TryParse, so the row is
        // skipped (the `!TryParse(...) continue` branch).
        const string xml =
            """
            <ValCurs Date="02.06.2026">
              <Valute><CharCode>AAA</CharCode><Nominal>abc</Nominal><Value>5.00</Value></Valute>
              <Valute><CharCode>USD</CharCode><Nominal>1</Nominal><Value>17.50</Value></Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(xml);

        rates.Should().ContainSingle();
        rates.Single().CharCode.Should().Be("USD");
    }

    [Fact]
    public void Parse_InvalidDateAttributeFormat_YieldsDefaultAsOf()
    {
        // Date present but not dd.MM.yyyy -> TryParseExact fails -> ParseAsOf
        // returns default (the final `return default;` line).
        const string xml =
            """
            <ValCurs Date="2026-06-02">
              <Valute><CharCode>USD</CharCode><Nominal>1</Nominal><Value>17.50</Value></Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(xml);

        rates.Should().ContainSingle();
        rates.Single().AsOf.Should().Be(default);
    }

    [Fact]
    public void Parse_MissingDateAttribute_YieldsDefaultAsOf()
    {
        const string xml =
            """
            <ValCurs>
              <Valute><CharCode>USD</CharCode><Nominal>1</Nominal><Value>17.50</Value></Valute>
            </ValCurs>
            """;

        IReadOnlyList<BnmRate> rates = BnmRateProvider.Parse(xml);

        rates.Should().ContainSingle();
        rates.Single().AsOf.Should().Be(default);
    }
}
