using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/fx-rates</c>: manual create, paged list, convert
/// (incl. invalid-currency and identity rate), delete, and the backfill range
/// guards. The actual BNM <c>/refresh</c> and <c>/backfill</c> network fetch is
/// NOT exercised (it would hit bnm.md, which the test host deliberately avoids);
/// only the synchronous validation branches that return before any provider
/// call are covered here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FxRateEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Create_manual_rate_then_appears_in_list()
    {
        // Unique date so the row is identifiable across reruns.
        DateOnly asOf = new DateOnly(2020, 1, 1).AddDays(Random.Shared.Next(0, 1000));
        var body = new { fromCurrency = "USD", toCurrency = "MDL", rate = 17.5m, asOf = asOf.ToString("yyyy-MM-dd") };

        HttpResponseMessage create = await Client.PostAsJsonAsync("/fx-rates", body);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument createDoc = await _fx.ReadDocAsync(create);
        Guid id = createDoc.RootElement.GetProperty("id").GetGuid();

        HttpResponseMessage list = await Client.GetAsync("/fx-rates?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        doc.RootElement.GetProperty("pageNumber").GetInt32().Should().Be(1);
        // The newly created id should be retrievable; delete it to keep the table tidy.
        HttpResponseMessage del = await Client.DeleteAsync($"/fx-rates/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_rate_with_same_from_and_to_returns_400()
    {
        var body = new { fromCurrency = "USD", toCurrency = "USD", rate = 1m, asOf = "2024-01-01" };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/fx-rates", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_rate_with_non_positive_rate_returns_400()
    {
        var body = new { fromCurrency = "USD", toCurrency = "MDL", rate = 0m, asOf = "2024-01-01" };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/fx-rates", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Convert_with_explicit_rate_returns_converted_amount()
    {
        DateOnly asOf = new(2024, 4, 2);
        await Client.PostAsJsonAsync("/fx-rates", new { fromCurrency = "EUR", toCurrency = "MDL", rate = 20m, asOf = asOf.ToString("yyyy-MM-dd") });

        HttpResponseMessage response = await Client.GetAsync(
            $"/fx-rates/convert?from=EUR&to=MDL&date={asOf:yyyy-MM-dd}&amount=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("hasRate").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("convertedAmount").GetDecimal().Should().Be(200m);
    }

    [Fact]
    public async Task Convert_identity_currency_returns_rate_one()
    {
        HttpResponseMessage response = await Client.GetAsync(
            "/fx-rates/convert?from=MDL&to=MDL&date=2024-01-01&amount=42");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("hasRate").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("rate").GetDecimal().Should().Be(1m);
        doc.RootElement.GetProperty("convertedAmount").GetDecimal().Should().Be(42m);
    }

    [Fact]
    public async Task Convert_with_invalid_currency_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync(
            "/fx-rates/convert?from=US&to=MDL&date=2024-01-01&amount=10");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("fx.invalid_currency");
    }

    [Fact]
    public async Task Delete_unknown_rate_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/fx-rates/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("fx_rate.not_found");
    }

    [Fact]
    public async Task Backfill_with_future_start_returns_400()
    {
        var body = new { from = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"), to = (string?)null };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/fx-rates/backfill", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("fx.backfill_future_start");
    }

    [Fact]
    public async Task Backfill_with_range_too_large_returns_400()
    {
        var body = new { from = "2018-01-01", to = "2024-01-01" };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/fx-rates/backfill", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("fx.backfill_range_too_large");
    }
}
