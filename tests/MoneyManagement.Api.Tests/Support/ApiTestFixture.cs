using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace MoneyManagement.Api.Tests.Support;

/// <summary>
/// Per-class helper that wraps the shared <see cref="CustomWebApplicationFactory"/>
/// host with the JSON options the app uses (enums as names) and convenience
/// methods for the data the endpoint tests repeatedly need: creating accounts,
/// categories, transactions, etc. with GUID-unique names so every test owns its
/// data and never asserts on global row counts. All writes hit the guarded
/// <c>money_management_inttest</c> DB only.
/// </summary>
public sealed class ApiTestFixture(CustomWebApplicationFactory factory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient Client { get; } = factory.CreateClient();

    public string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    public async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, Json)
            ?? throw new InvalidOperationException($"Response body deserialized to null: {body}");
    }

    public async Task<JsonDocument> ReadDocAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    // ---- Account helpers -------------------------------------------------

    public async Task<Guid> CreateAccountAsync(
        string? name = null,
        string type = "Cash",
        decimal balance = 0m,
        string currency = "MDL",
        string openingDate = "2024-01-01",
        string? notes = null)
    {
        var body = new
        {
            name = name ?? Unique("Acct"),
            type,
            balance,
            currency,
            openingDate,
            notes,
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/accounts", body);
        response.IsSuccessStatusCode.Should().BeTrue(
            $"account create should succeed: {await response.Content.ReadAsStringAsync()}");

        using JsonDocument doc = await ReadDocAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ---- Category helpers ------------------------------------------------

    public async Task<Guid> CreateCategoryAsync(string flow = "Expense", string? name = null)
    {
        var body = new { name = name ?? Unique("Cat"), flow, parentId = (Guid?)null, color = "#112233", icon = (string?)null };
        HttpResponseMessage response = await Client.PostAsJsonAsync("/categories", body);
        response.IsSuccessStatusCode.Should().BeTrue(
            $"category create should succeed: {await response.Content.ReadAsStringAsync()}");
        using JsonDocument doc = await ReadDocAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ---- Transaction helpers --------------------------------------------

    public async Task<Guid> CreateTransactionAsync(
        Guid accountId,
        string direction = "Expense",
        decimal amount = 10m,
        string transactionDate = "2024-06-01",
        Guid? categoryId = null,
        string? description = null,
        string? notes = null)
    {
        var body = new
        {
            accountId,
            transactionDate,
            direction,
            amount,
            description = description ?? Unique("Tx"),
            categoryId,
            notes,
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transactions", body);
        response.IsSuccessStatusCode.Should().BeTrue(
            $"transaction create should succeed: {await response.Content.ReadAsStringAsync()}");
        using JsonDocument doc = await ReadDocAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }
}
