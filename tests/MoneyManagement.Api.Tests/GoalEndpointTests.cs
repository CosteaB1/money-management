using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/goals</c>: create (manual + linked-account + unknown
/// account 404 + non-positive target 400), list, detail projection, update,
/// manual-saved patch (incl. the linked-mode 400 and unknown-id 404), and archive.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GoalEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    private async Task<Guid> CreateManualGoalAsync(decimal target = 1000m, string? name = null)
    {
        var body = new { name = name ?? _fx.Unique("Goal"), targetAmount = target, targetDate = (string?)null, linkedAccountId = (Guid?)null };
        HttpResponseMessage response = await Client.PostAsJsonAsync("/goals", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using JsonDocument doc = await _fx.ReadDocAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_manual_goal_then_list_shows_it()
    {
        string name = _fx.Unique("Goal");
        Guid id = await CreateManualGoalAsync(2000m, name);

        HttpResponseMessage list = await Client.GetAsync("/goals");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == id);
        match.GetProperty("name").GetString().Should().Be(name);
        match.GetProperty("targetAmount").GetDecimal().Should().Be(2000m);
        match.GetProperty("isLinkedMode").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_linked_goal_against_account_sets_linked_mode()
    {
        Guid accountId = await _fx.CreateAccountAsync(type: "BankDeposit", balance: 300m, currency: "MDL");
        var body = new { name = _fx.Unique("Goal"), targetAmount = 5000m, targetDate = (string?)null, linkedAccountId = accountId };

        HttpResponseMessage create = await Client.PostAsJsonAsync("/goals", body);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument createDoc = await _fx.ReadDocAsync(create);
        Guid id = createDoc.RootElement.GetProperty("id").GetGuid();

        HttpResponseMessage detail = await Client.GetAsync($"/goals/{id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = await _fx.ReadDocAsync(detail);
        doc.RootElement.GetProperty("isLinkedMode").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("linkedAccountId").GetGuid().Should().Be(accountId);
        doc.RootElement.TryGetProperty("pace", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("contributions", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("savedHistory", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_goal_with_unknown_linked_account_returns_404()
    {
        var body = new { name = _fx.Unique("Goal"), targetAmount = 100m, targetDate = (string?)null, linkedAccountId = Guid.NewGuid() };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/goals", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Create_goal_with_non_positive_target_returns_400()
    {
        var body = new { name = _fx.Unique("Goal"), targetAmount = 0m, targetDate = (string?)null, linkedAccountId = (Guid?)null };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/goals", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_goal_detail_for_unknown_id_returns_404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/goals/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("savings_goal.not_found");
    }

    [Fact]
    public async Task Update_goal_changes_name_and_target()
    {
        Guid id = await CreateManualGoalAsync(1000m);
        string newName = _fx.Unique("Renamed");

        var body = new { name = newName, targetAmount = 3333m, targetDate = (string?)null, linkedAccountId = (Guid?)null };
        HttpResponseMessage update = await Client.PutAsJsonAsync($"/goals/{id}", body);
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage detail = await Client.GetAsync($"/goals/{id}");
        using JsonDocument doc = await _fx.ReadDocAsync(detail);
        doc.RootElement.GetProperty("name").GetString().Should().Be(newName);
        doc.RootElement.GetProperty("targetAmount").GetDecimal().Should().Be(3333m);
    }

    [Fact]
    public async Task Update_unknown_goal_returns_404()
    {
        var body = new { name = _fx.Unique("X"), targetAmount = 10m, targetDate = (string?)null, linkedAccountId = (Guid?)null };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/goals/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_manual_saved_on_manual_goal_updates_saved()
    {
        Guid id = await CreateManualGoalAsync(1000m);

        HttpResponseMessage patch = await Client.PatchAsJsonAsync($"/goals/{id}/manual-saved", new { amount = 400m });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage detail = await Client.GetAsync($"/goals/{id}");
        using JsonDocument doc = await _fx.ReadDocAsync(detail);
        doc.RootElement.GetProperty("saved").GetDecimal().Should().Be(400m);
    }

    [Fact]
    public async Task Patch_manual_saved_on_linked_goal_returns_400()
    {
        Guid accountId = await _fx.CreateAccountAsync(type: "BankDeposit", balance: 0m, currency: "MDL");
        var create = new { name = _fx.Unique("Goal"), targetAmount = 1000m, targetDate = (string?)null, linkedAccountId = accountId };
        HttpResponseMessage createResponse = await Client.PostAsJsonAsync("/goals", create);
        using JsonDocument createDoc = await _fx.ReadDocAsync(createResponse);
        Guid id = createDoc.RootElement.GetProperty("id").GetGuid();

        HttpResponseMessage patch = await Client.PatchAsJsonAsync($"/goals/{id}/manual-saved", new { amount = 50m });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(patch);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("savings_goal.not_in_manual_mode");
    }

    [Fact]
    public async Task Patch_manual_saved_on_unknown_goal_returns_404()
    {
        HttpResponseMessage response = await Client.PatchAsJsonAsync($"/goals/{Guid.NewGuid()}/manual-saved", new { amount = 50m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archive_goal_hides_it_from_list()
    {
        Guid id = await CreateManualGoalAsync();

        HttpResponseMessage archive = await Client.DeleteAsync($"/goals/{id}");
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync("/goals");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        doc.RootElement.EnumerateArray().Any(g => g.GetProperty("id").GetGuid() == id).Should().BeFalse();
    }

    [Fact]
    public async Task Archive_unknown_goal_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/goals/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
