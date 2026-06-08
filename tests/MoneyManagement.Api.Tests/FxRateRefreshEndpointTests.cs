using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MoneyManagement.Application.Abstractions.FxRates;
using NSubstitute;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Exercises <c>POST /fx-rates/refresh</c> end-to-end WITHOUT touching the
/// network: the real <see cref="IBnmRateProvider"/> (which fetches bnm.md) is
/// replaced with a fake that returns canned rates, so the endpoint's
/// command-dispatch happy path is covered offline. Uses its own customised
/// factory so the swap doesn't affect the shared integration collection.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FxRateRefreshEndpointTests(CustomWebApplicationFactory factory)
{
    [Fact]
    public async Task Refresh_with_fake_provider_returns_counts()
    {
        IBnmRateProvider fakeProvider = Substitute.For<IBnmRateProvider>();
        fakeProvider
            .GetRatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BnmRate>>(
            [
                new BnmRate("USD", 17.50m, new DateOnly(2026, 5, 22)),
                new BnmRate("EUR", 19.00m, new DateOnly(2026, 5, 22)),
            ]));

        using WebApplicationFactory<Program> customized = factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBnmRateProvider>();
                    services.AddSingleton(fakeProvider);
                }));

        using HttpClient client = customized.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/fx-rates/refresh",
            new { date = "2026-05-22" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // The response carries fetched/inserted/updated/skipped counts; fetched
        // must reflect the two canned rates.
        doc.RootElement.GetProperty("fetched").GetInt32().Should().Be(2);
    }
}
