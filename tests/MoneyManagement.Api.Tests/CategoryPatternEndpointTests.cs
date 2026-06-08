using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/category-patterns</c>: list, create (happy +
/// duplicate-keyword 409 + unknown-category 404), update, and delete.
/// Keywords are GUID-unique so they never collide with the seeded patterns or
/// sibling tests; keyword matching is case-insensitive (normalized upper).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CategoryPatternEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    private static readonly Guid GroceriesExpenseId = new("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Create_then_list_returns_the_new_pattern_with_normalized_keyword()
    {
        string keyword = $"KW{Guid.NewGuid():N}";
        var body = new { keyword, categoryId = GroceriesExpenseId };

        HttpResponseMessage create = await Client.PostAsJsonAsync("/category-patterns", body);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument createDoc = await _fx.ReadDocAsync(create);
        Guid id = createDoc.RootElement.GetProperty("id").GetGuid();

        HttpResponseMessage list = await Client.GetAsync("/category-patterns");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        match.GetProperty("keyword").GetString().Should().Be(keyword.ToUpperInvariant());
        match.GetProperty("categoryId").GetGuid().Should().Be(GroceriesExpenseId);
    }

    [Fact]
    public async Task Create_pattern_with_duplicate_keyword_returns_409()
    {
        string keyword = $"DUP{Guid.NewGuid():N}";
        var body = new { keyword, categoryId = GroceriesExpenseId };

        HttpResponseMessage first = await Client.PostAsJsonAsync("/category-patterns", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Same keyword in different casing — still collides after normalization.
        var dup = new { keyword = keyword.ToLowerInvariant(), categoryId = GroceriesExpenseId };
        HttpResponseMessage second = await Client.PostAsJsonAsync("/category-patterns", dup);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using JsonDocument problem = await _fx.ReadDocAsync(second);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.pattern_keyword_exists");
    }

    [Fact]
    public async Task Create_pattern_with_unknown_category_returns_404()
    {
        var body = new { keyword = $"KW{Guid.NewGuid():N}", categoryId = Guid.NewGuid() };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/category-patterns", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.not_found");
    }

    [Fact]
    public async Task Update_pattern_changes_keyword()
    {
        string keyword = $"KW{Guid.NewGuid():N}";
        HttpResponseMessage create = await Client.PostAsJsonAsync(
            "/category-patterns", new { keyword, categoryId = GroceriesExpenseId });
        using JsonDocument createDoc = await _fx.ReadDocAsync(create);
        Guid id = createDoc.RootElement.GetProperty("id").GetGuid();

        string newKeyword = $"KW{Guid.NewGuid():N}";
        HttpResponseMessage update = await Client.PutAsJsonAsync(
            $"/category-patterns/{id}", new { keyword = newKeyword, categoryId = GroceriesExpenseId });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync("/category-patterns");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        match.GetProperty("keyword").GetString().Should().Be(newKeyword.ToUpperInvariant());
    }

    [Fact]
    public async Task Update_unknown_pattern_returns_404()
    {
        HttpResponseMessage response = await Client.PutAsJsonAsync(
            $"/category-patterns/{Guid.NewGuid()}", new { keyword = $"KW{Guid.NewGuid():N}", categoryId = GroceriesExpenseId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.pattern_not_found");
    }

    [Fact]
    public async Task Delete_pattern_removes_it()
    {
        string keyword = $"KW{Guid.NewGuid():N}";
        HttpResponseMessage create = await Client.PostAsJsonAsync(
            "/category-patterns", new { keyword, categoryId = GroceriesExpenseId });
        using JsonDocument createDoc = await _fx.ReadDocAsync(create);
        Guid id = createDoc.RootElement.GetProperty("id").GetGuid();

        HttpResponseMessage del = await Client.DeleteAsync($"/category-patterns/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync("/category-patterns");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        doc.RootElement.EnumerateArray().Any(p => p.GetProperty("id").GetGuid() == id).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_unknown_pattern_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/category-patterns/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
