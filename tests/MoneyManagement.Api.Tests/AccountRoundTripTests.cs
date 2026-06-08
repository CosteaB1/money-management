using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Happy-path write/read round-trip exercising the real Postgres DB, EF Core
/// persistence, and the <c>EfFxConverter</c> (balance + MDL conversion). Uses a
/// unique account name per run so it never collides with sibling tests or reruns.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountRoundTripTests(CustomWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_then_list_returns_the_new_account_with_correct_fields()
    {
        string uniqueName = $"IntTest Account {Guid.NewGuid():N}";

        var create = new
        {
            name = uniqueName,
            type = "Cash",
            balance = 1234.56m,
            currency = "MDL",
            openingDate = "2026-01-15",
            notes = "round-trip",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/accounts", create);
        createResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Guid newId = created.RootElement.GetProperty("id").GetGuid();
        newId.Should().NotBeEmpty();

        HttpResponseMessage listResponse = await _client.GetAsync("/accounts");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());

        JsonElement match = list.RootElement
            .EnumerateArray()
            .Single(a => a.GetProperty("id").GetGuid() == newId);

        match.GetProperty("name").GetString().Should().Be(uniqueName);
        match.GetProperty("type").GetString().Should().Be("Cash");
        match.GetProperty("currency").GetString().Should().Be("MDL");
        match.GetProperty("balance").GetDecimal().Should().Be(1234.56m);
        // MDL account → BalanceMdl mirrors balance (no FX conversion needed).
        match.GetProperty("balanceMdl").GetDecimal().Should().Be(1234.56m);
        match.GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }
}
