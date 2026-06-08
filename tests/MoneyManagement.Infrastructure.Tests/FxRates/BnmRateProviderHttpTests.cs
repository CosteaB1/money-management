using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Infrastructure.FxRates;

namespace MoneyManagement.Infrastructure.Tests.FxRates;

/// <summary>
/// Exercises the HTTP fetch path of <see cref="BnmRateProvider"/> with a stubbed
/// <see cref="HttpMessageHandler"/> — no live network. The contract is:
/// HTTP 200 + valid XML -&gt; parsed rates; any non-success status, timeout, or
/// transport failure -&gt; empty list (caller treats "no rates" as a non-error);
/// genuine caller cancellation -&gt; rethrown.
/// </summary>
public sealed class BnmRateProviderHttpTests
{
    private const string WellFormed =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ValCurs Date="02.06.2026" name="Cursul oficial">
          <Valute ID="47"><CharCode>USD</CharCode><Nominal>1</Nominal><Value>17.5000</Value></Valute>
          <Valute ID="84"><CharCode>EUR</CharCode><Nominal>1</Nominal><Value>19.2500</Value></Valute>
        </ValCurs>
        """;

    private static BnmRateProvider Build(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        IOptions<FxAutoFetchOptions> options = Options.Create(new FxAutoFetchOptions
        {
            BnmBaseUrl = "https://fake.test/official_exchange_rates",
        });
        return new BnmRateProvider(httpClient, options, NullLogger<BnmRateProvider>.Instance);
    }

    [Fact]
    public async Task GetRatesAsync_Http200WithValidXml_ReturnsParsedRates()
    {
        var handler = StubHttpMessageHandler.ReturnsOk(WellFormed);
        BnmRateProvider provider = Build(handler);

        IReadOnlyList<BnmRate> rates = await provider.GetRatesAsync(
            new DateOnly(2026, 6, 2), CancellationToken.None);

        rates.Should().HaveCount(2);
        rates.Should().ContainSingle(r => r.CharCode == "USD" && r.Rate == 17.5000m);
        rates.Should().ContainSingle(r => r.CharCode == "EUR" && r.Rate == 19.2500m);
    }

    [Fact]
    public async Task GetRatesAsync_FormatsTheBnmUrlWithDayDotMonthDotYear()
    {
        var handler = StubHttpMessageHandler.ReturnsOk(WellFormed);
        BnmRateProvider provider = Build(handler);

        await provider.GetRatesAsync(new DateOnly(2026, 1, 7), CancellationToken.None);

        // BNM expects DD.MM.YYYY, not ISO — guard the date formatting.
        handler.LastRequestUri!.Query.Should().Contain("date=07.01.2026");
        handler.LastRequestUri!.Query.Should().Contain("get_xml=1");
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.NotFound)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.BadGateway)]
    public async Task GetRatesAsync_NonSuccessStatus_ReturnsEmpty(System.Net.HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.ReturnsStatus(status);
        BnmRateProvider provider = Build(handler);

        IReadOnlyList<BnmRate> rates = await provider.GetRatesAsync(
            new DateOnly(2026, 6, 2), CancellationToken.None);

        rates.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRatesAsync_Timeout_ReturnsEmpty()
    {
        // HttpClient timeouts surface as TaskCanceledException with an unrelated
        // token; the provider must swallow these to "no rates".
        var handler = StubHttpMessageHandler.ThrowsTimeout();
        BnmRateProvider provider = Build(handler);

        IReadOnlyList<BnmRate> rates = await provider.GetRatesAsync(
            new DateOnly(2026, 6, 2), CancellationToken.None);

        rates.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRatesAsync_TransportFailure_ReturnsEmpty()
    {
        var handler = StubHttpMessageHandler.ThrowsHttpRequestException();
        BnmRateProvider provider = Build(handler);

        IReadOnlyList<BnmRate> rates = await provider.GetRatesAsync(
            new DateOnly(2026, 6, 2), CancellationToken.None);

        rates.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRatesAsync_CallerCancellation_Rethrows()
    {
        var handler = StubHttpMessageHandler.ThrowsOnCancellation();
        BnmRateProvider provider = Build(handler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.GetRatesAsync(new DateOnly(2026, 6, 2), cts.Token);

        // Real cancellation must bubble up — never silently become "no data".
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetRatesAsync_Http200WithEmptyBody_ReturnsEmpty()
    {
        var handler = StubHttpMessageHandler.ReturnsOk(string.Empty);
        BnmRateProvider provider = Build(handler);

        IReadOnlyList<BnmRate> rates = await provider.GetRatesAsync(
            new DateOnly(2026, 6, 2), CancellationToken.None);

        rates.Should().BeEmpty();
    }
}
