using System.Net;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/dashboard</c>: summary (default month + explicit
/// month + malformed-month 400) and net-worth-trend (default + explicit months +
/// out-of-range 400). These exercise the large Application projection handlers
/// and the EfFxConverter conversion path.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Summary_default_month_returns_shape()
    {
        HttpResponseMessage response = await Client.GetAsync("/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.TryGetProperty("month", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("income", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("expense", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("net", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("savingsRate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Summary_explicit_month_echoes_window()
    {
        HttpResponseMessage response = await Client.GetAsync("/dashboard/summary?month=2024-03");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("month").GetString().Should().Be("2024-03");
    }

    [Fact]
    public async Task Summary_with_malformed_month_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync("/dashboard/summary?month=2024-13");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("type").GetString().Should().Be("dashboard.invalid_month");
    }

    [Fact]
    public async Task Summary_reflects_a_known_income_transaction_in_window()
    {
        // Own account + an income tx in a fixed past month; summary for that month
        // must reflect at least our income.
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        await _fx.CreateTransactionAsync(accountId, direction: "Income", amount: 123m, transactionDate: "2024-07-10",
            categoryId: new Guid("00000000-0000-0000-0000-000000000007"));

        HttpResponseMessage response = await Client.GetAsync("/dashboard/summary?month=2024-07");
        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("income").GetDecimal().Should().BeGreaterThanOrEqualTo(123m);
    }

    [Fact]
    public async Task Net_worth_trend_default_returns_points()
    {
        HttpResponseMessage response = await Client.GetAsync("/dashboard/net-worth-trend");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetArrayLength().Should().Be(6, "default months is 6");
        foreach (JsonElement point in doc.RootElement.EnumerateArray())
        {
            point.TryGetProperty("netWorthMdl", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Net_worth_trend_with_explicit_months_returns_that_many_points()
    {
        HttpResponseMessage response = await Client.GetAsync("/dashboard/net-worth-trend?months=3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Net_worth_trend_out_of_range_months_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync("/dashboard/net-worth-trend?months=99");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("dashboard.months_out_of_range");
    }
}
