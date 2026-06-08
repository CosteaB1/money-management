using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/budgets</c>: create (happy + duplicate-active 409 +
/// unknown-category 404 + non-positive limit 400), list for a year/month, update
/// limit, archive, and rebuild-periods (single + all). Each budget targets a
/// fresh GUID-named category so the "one active budget per category" rule never
/// collides across tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BudgetEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    private async Task<Guid> CreateBudgetAsync(Guid categoryId, decimal limit = 1000m)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/budgets", new { categoryId, monthlyLimit = limit });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using JsonDocument doc = await _fx.ReadDocAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_then_list_shows_budget_for_category()
    {
        Guid categoryId = await _fx.CreateCategoryAsync(flow: "Expense");
        Guid budgetId = await CreateBudgetAsync(categoryId, 1500m);

        HttpResponseMessage list = await Client.GetAsync("/budgets?year=2024&month=6");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == budgetId);
        match.GetProperty("categoryId").GetGuid().Should().Be(categoryId);
        match.GetProperty("monthlyLimit").GetDecimal().Should().Be(1500m);
        match.GetProperty("year").GetInt32().Should().Be(2024);
        match.GetProperty("month").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Create_duplicate_active_budget_returns_409()
    {
        Guid categoryId = await _fx.CreateCategoryAsync(flow: "Expense");
        await CreateBudgetAsync(categoryId);

        HttpResponseMessage second = await Client.PostAsJsonAsync("/budgets", new { categoryId, monthlyLimit = 200m });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using JsonDocument problem = await _fx.ReadDocAsync(second);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("budget.already_exists_for_category");
    }

    [Fact]
    public async Task Create_budget_for_unknown_category_returns_404()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/budgets", new { categoryId = Guid.NewGuid(), monthlyLimit = 100m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.not_found");
    }

    [Fact]
    public async Task Create_budget_with_non_positive_limit_returns_400()
    {
        Guid categoryId = await _fx.CreateCategoryAsync(flow: "Expense");
        HttpResponseMessage response = await Client.PostAsJsonAsync("/budgets", new { categoryId, monthlyLimit = 0m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_budget_limit_persists()
    {
        Guid categoryId = await _fx.CreateCategoryAsync(flow: "Expense");
        Guid budgetId = await CreateBudgetAsync(categoryId, 500m);

        HttpResponseMessage update = await Client.PutAsJsonAsync($"/budgets/{budgetId}", new { monthlyLimit = 750m });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync("/budgets");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == budgetId);
        match.GetProperty("monthlyLimit").GetDecimal().Should().Be(750m);
    }

    [Fact]
    public async Task Update_unknown_budget_returns_404()
    {
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/budgets/{Guid.NewGuid()}", new { monthlyLimit = 10m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("budget.not_found");
    }

    [Fact]
    public async Task Archive_budget_then_category_can_get_a_new_one()
    {
        Guid categoryId = await _fx.CreateCategoryAsync(flow: "Expense");
        Guid budgetId = await CreateBudgetAsync(categoryId);

        HttpResponseMessage archive = await Client.DeleteAsync($"/budgets/{budgetId}");
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // After archiving, the "one active per category" rule no longer blocks a new one.
        HttpResponseMessage recreate = await Client.PostAsJsonAsync("/budgets", new { categoryId, monthlyLimit = 99m });
        recreate.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Archive_unknown_budget_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/budgets/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rebuild_single_budget_periods_returns_counts()
    {
        Guid categoryId = await _fx.CreateCategoryAsync(flow: "Expense");
        Guid budgetId = await CreateBudgetAsync(categoryId);

        HttpResponseMessage response = await Client.PostAsync($"/budgets/{budgetId}/rebuild-periods", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("budgetsRebuilt").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        doc.RootElement.TryGetProperty("periodsAffected", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Rebuild_all_budget_periods_returns_counts()
    {
        HttpResponseMessage response = await Client.PostAsync("/budgets/rebuild-all-periods", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.TryGetProperty("budgetsRebuilt", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("periodsAffected", out _).Should().BeTrue();
    }
}
