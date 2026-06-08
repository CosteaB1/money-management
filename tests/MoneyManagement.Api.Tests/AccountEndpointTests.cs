using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/accounts</c>: create, list (incl. includeArchived),
/// detail projection, archive / unarchive, permanent delete (incl. the
/// has-linked-records 409), and balance-change adjustments. Every test owns
/// GUID-named entities and asserts only on those.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Create_account_with_invalid_currency_returns_400()
    {
        var body = new { name = _fx.Unique("Acct"), type = "Cash", balance = 0m, currency = "mdl", openingDate = "2024-01-01", notes = (string?)null };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/accounts", body);

        // The FluentValidation layer rejects a non-uppercase ISO code before the
        // domain rule runs, so the surfaced code is the property name.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("Currency");
    }

    [Fact]
    public async Task Create_non_credit_card_with_negative_balance_returns_400()
    {
        var body = new { name = _fx.Unique("Acct"), type = "Cash", balance = -5m, currency = "MDL", openingDate = "2024-01-01", notes = (string?)null };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/accounts", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.negative_balance_for_non_credit_card");
    }

    [Fact]
    public async Task Get_account_detail_returns_projection_for_known_id()
    {
        Guid id = await _fx.CreateAccountAsync(type: "Brokerage", balance: 1000m, currency: "MDL");

        HttpResponseMessage response = await Client.GetAsync($"/accounts/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("id").GetGuid().Should().Be(id);
        doc.RootElement.GetProperty("type").GetString().Should().Be("Brokerage");
        doc.RootElement.GetProperty("balance").GetDecimal().Should().Be(1000m);
        doc.RootElement.TryGetProperty("allTime", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("yearToDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_account_detail_for_unknown_id_returns_404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/accounts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Update_account_renames_and_edits_notes_and_persists()
    {
        Guid id = await _fx.CreateAccountAsync(notes: "before");
        string newName = _fx.Unique("Renamed");

        var body = new { name = newName, notes = "after" };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/accounts/{id}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage detail = await Client.GetAsync($"/accounts/{id}");
        using JsonDocument doc = await _fx.ReadDocAsync(detail);
        doc.RootElement.GetProperty("name").GetString().Should().Be(newName);
        doc.RootElement.GetProperty("notes").GetString().Should().Be("after");
    }

    [Fact]
    public async Task Update_unknown_account_returns_404()
    {
        var body = new { name = _fx.Unique("Whatever"), notes = (string?)null };

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/accounts/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Update_account_with_blank_name_returns_400()
    {
        Guid id = await _fx.CreateAccountAsync();

        var body = new { name = "", notes = (string?)null };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/accounts/{id}", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("Name");
    }

    [Fact]
    public async Task Archive_then_unarchive_round_trips_isArchived_flag()
    {
        Guid id = await _fx.CreateAccountAsync();

        HttpResponseMessage archive = await Client.DeleteAsync($"/accounts/{id}");
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Default list hides archived.
        HttpResponseMessage list = await Client.GetAsync("/accounts");
        using (JsonDocument listDoc = await _fx.ReadDocAsync(list))
        {
            listDoc.RootElement.EnumerateArray().Any(a => a.GetProperty("id").GetGuid() == id)
                .Should().BeFalse("archived accounts are hidden from the default list");
        }

        // includeArchived=true surfaces it as archived.
        HttpResponseMessage listAll = await Client.GetAsync("/accounts?includeArchived=true");
        using (JsonDocument allDoc = await _fx.ReadDocAsync(listAll))
        {
            JsonElement match = allDoc.RootElement.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
            match.GetProperty("isArchived").GetBoolean().Should().BeTrue();
        }

        HttpResponseMessage unarchive = await Client.PostAsync($"/accounts/{id}/unarchive", null);
        unarchive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage detail = await Client.GetAsync($"/accounts/{id}");
        using JsonDocument detailDoc = await _fx.ReadDocAsync(detail);
        detailDoc.RootElement.GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Archive_unknown_account_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/accounts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Permanent_delete_with_no_links_succeeds()
    {
        Guid id = await _fx.CreateAccountAsync();

        HttpResponseMessage response = await Client.DeleteAsync($"/accounts/{id}/permanent");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage detail = await Client.GetAsync($"/accounts/{id}");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Permanent_delete_unknown_account_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/accounts/{Guid.NewGuid()}/permanent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Permanent_delete_with_linked_transaction_returns_409_has_linked_records()
    {
        Guid id = await _fx.CreateAccountAsync();
        await _fx.CreateTransactionAsync(id);

        HttpResponseMessage response = await Client.DeleteAsync($"/accounts/{id}/permanent");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.has_linked_records");
    }

    [Fact]
    public async Task Balance_change_investment_on_brokerage_creates_transaction()
    {
        Guid id = await _fx.CreateAccountAsync(type: "Brokerage", balance: 0m, currency: "MDL");

        var body = new { kind = "Investment", value = 500m, date = "2024-06-01", notes = "seed capital" };
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/accounts/{id}/balance-changes", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("transactionId").GetGuid().Should().NotBeEmpty();
        doc.RootElement.GetProperty("delta").GetDecimal().Should().Be(500m);
    }

    [Fact]
    public async Task Balance_change_on_cash_account_returns_400_not_eligible()
    {
        Guid id = await _fx.CreateAccountAsync(type: "Cash");

        var body = new { kind = "Investment", value = 100m, date = "2024-06-01", notes = (string?)null };
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/accounts/{id}/balance-changes", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transaction.adjustment_account_type_not_eligible");
    }

    [Fact]
    public async Task Balance_change_adjustment_to_same_balance_returns_400_delta_zero()
    {
        Guid id = await _fx.CreateAccountAsync(type: "BankDeposit", balance: 250m, currency: "MDL");

        var body = new { kind = "Adjustment", value = 250m, date = "2024-06-01", notes = (string?)null };
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/accounts/{id}/balance-changes", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transaction.adjustment_delta_zero");
    }

    [Fact]
    public async Task Balance_change_on_unknown_account_returns_404()
    {
        var body = new { kind = "Investment", value = 100m, date = "2024-06-01", notes = (string?)null };
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/accounts/{Guid.NewGuid()}/balance-changes", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }
}
