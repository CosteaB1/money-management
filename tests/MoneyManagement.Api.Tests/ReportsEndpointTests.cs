using System.Net;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for all five <c>/reports/*</c> routes: monthly-summary,
/// category-breakdown, balance-over-time, top-payees, and the transactions.csv
/// stream. Covers happy paths plus the documented range / month validation
/// errors. These drive the heavy Application projection handlers and the
/// EfFxConverter conversion path.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReportsEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    private static readonly Guid GroceriesExpenseId = new("00000000-0000-0000-0000-000000000001");

    // ---- monthly-summary -------------------------------------------------

    [Fact]
    public async Task Monthly_summary_default_window_returns_series()
    {
        HttpResponseMessage response = await Client.GetAsync("/reports/monthly-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Monthly_summary_explicit_range_returns_expected_count()
    {
        HttpResponseMessage response = await Client.GetAsync("/reports/monthly-summary?from=2024-01&to=2024-03");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetArrayLength().Should().Be(3);
        doc.RootElement[0].GetProperty("month").GetString().Should().Be("2024-01");
    }

    [Fact]
    public async Task Monthly_summary_with_malformed_month_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync("/reports/monthly-summary?from=nope");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("type").GetString().Should().Be("reports.invalid_month");
    }

    [Fact]
    public async Task Monthly_summary_with_malformed_to_month_returns_400()
    {
        // Drives the `to`-parameter month-parse guard (the `from` guard is covered
        // above; this exercises the second branch).
        HttpResponseMessage response = await Client.GetAsync("/reports/monthly-summary?from=2024-01&to=nope");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("type").GetString().Should().Be("reports.invalid_month");
    }

    // ---- category-breakdown ----------------------------------------------

    [Fact]
    public async Task Category_breakdown_returns_items_for_known_expense()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 80m, transactionDate: "2024-08-05", categoryId: GroceriesExpenseId);

        HttpResponseMessage response = await Client.GetAsync(
            "/reports/category-breakdown?from=2024-08-01&to=2024-08-31&direction=Expense");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("direction").GetString().Should().Be("Expense");
        doc.RootElement.GetProperty("totalMdl").GetDecimal().Should().BeGreaterThanOrEqualTo(80m);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Category_breakdown_with_from_after_to_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync(
            "/reports/category-breakdown?from=2024-12-31&to=2024-01-01&direction=Expense");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("reports.range_out_of_bounds");
    }

    // ---- balance-over-time -----------------------------------------------

    [Fact]
    public async Task Balance_over_time_monthly_returns_points()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 500m);

        HttpResponseMessage response = await Client.GetAsync(
            $"/reports/balance-over-time?accountId={accountId}&from=2024-01-01&to=2024-03-31&interval=Monthly");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement[0].TryGetProperty("balance", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Balance_over_time_for_unknown_account_returns_404()
    {
        HttpResponseMessage response = await Client.GetAsync(
            $"/reports/balance-over-time?accountId={Guid.NewGuid()}&from=2024-01-01&to=2024-03-31");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Balance_over_time_daily_over_huge_range_returns_400_interval_too_fine()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);

        HttpResponseMessage response = await Client.GetAsync(
            $"/reports/balance-over-time?accountId={accountId}&from=2015-01-01&to=2024-01-01&interval=Daily");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("reports.interval_too_fine");
    }

    // ---- top-payees ------------------------------------------------------

    [Fact]
    public async Task Top_payees_returns_aggregated_rows()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        string payee = _fx.Unique("Payee");
        await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 30m, transactionDate: "2024-09-01", description: payee);
        await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 20m, transactionDate: "2024-09-02", description: payee);

        HttpResponseMessage response = await Client.GetAsync(
            "/reports/top-payees?from=2024-09-01&to=2024-09-30&direction=Expense&limit=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        JsonElement match = doc.RootElement.EnumerateArray()
            .Single(p => string.Equals(p.GetProperty("originalDescription").GetString(), payee, StringComparison.OrdinalIgnoreCase));
        match.GetProperty("transactionCount").GetInt32().Should().Be(2);
        match.GetProperty("amountMdl").GetDecimal().Should().Be(50m);
    }

    [Fact]
    public async Task Top_payees_with_from_after_to_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync(
            "/reports/top-payees?from=2024-12-01&to=2024-01-01&direction=Expense");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- transactions.csv ------------------------------------------------

    [Fact]
    public async Task Export_transactions_csv_streams_with_header_and_known_row()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        string desc = _fx.Unique("CsvTx");
        await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 12.34m, transactionDate: "2024-10-10", description: desc);

        HttpResponseMessage response = await Client.GetAsync($"/reports/transactions.csv?accountId={accountId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        string csv = await response.Content.ReadAsStringAsync();
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("transaction_date,account,category,direction,amount,currency,amount_mdl,description");
        csv.Should().Contain(desc);
        csv.Should().Contain("12.34");
    }
}
