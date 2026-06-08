using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/categories</c>: create, list (incl. the seeded
/// reference rows + includeArchived), update, and archive (incl. unknown-id 404).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CategoryEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Create_then_list_returns_the_new_category()
    {
        string name = _fx.Unique("Cat");
        Guid id = await _fx.CreateCategoryAsync(flow: "Expense", name: name);

        HttpResponseMessage list = await Client.GetAsync("/categories");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        match.GetProperty("name").GetString().Should().Be(name);
        match.GetProperty("flow").GetString().Should().Be("Expense");
    }

    [Fact]
    public async Task Create_category_with_invalid_color_returns_400()
    {
        var body = new { name = _fx.Unique("Cat"), flow = "Expense", parentId = (Guid?)null, color = "red", icon = (string?)null };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/categories", body);

        // The FluentValidation Color regex rejects "red" before the domain rule,
        // so the surfaced code is the property name.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("Color");
    }

    [Fact]
    public async Task Create_category_with_unknown_parent_returns_404()
    {
        var body = new { name = _fx.Unique("Cat"), flow = "Expense", parentId = Guid.NewGuid(), color = "#112233", icon = (string?)null };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/categories", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.parent_not_found");
    }

    [Fact]
    public async Task Update_category_changes_name_and_color()
    {
        Guid id = await _fx.CreateCategoryAsync(flow: "Expense");
        string newName = _fx.Unique("Renamed");

        var body = new { name = newName, flow = "Expense", color = "#445566" };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/categories/{id}", body);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync("/categories");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        match.GetProperty("name").GetString().Should().Be(newName);
    }

    [Fact]
    public async Task Update_unknown_category_returns_404()
    {
        var body = new { name = _fx.Unique("X"), flow = "Expense", color = (string?)null };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/categories/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.not_found");
    }

    [Fact]
    public async Task Archive_category_hides_it_from_default_list()
    {
        Guid id = await _fx.CreateCategoryAsync(flow: "Expense");

        HttpResponseMessage archive = await Client.DeleteAsync($"/categories/{id}");
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync("/categories");
        using (JsonDocument doc = await _fx.ReadDocAsync(list))
        {
            doc.RootElement.EnumerateArray().Any(c => c.GetProperty("id").GetGuid() == id)
                .Should().BeFalse("archived categories are hidden by default");
        }

        HttpResponseMessage listAll = await Client.GetAsync("/categories?includeArchived=true");
        using JsonDocument allDoc = await _fx.ReadDocAsync(listAll);
        allDoc.RootElement.EnumerateArray().Any(c => c.GetProperty("id").GetGuid() == id)
            .Should().BeTrue("includeArchived surfaces archived categories");
    }

    [Fact]
    public async Task Archive_unknown_category_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/categories/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
