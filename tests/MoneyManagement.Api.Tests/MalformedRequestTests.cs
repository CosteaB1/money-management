using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Pins the F-3.1 fix: malformed request bodies must produce <c>400 Bad Request</c>
/// (via <c>GlobalExceptionHandler</c> mapping <c>BadHttpRequestException</c> /
/// <c>JsonException</c>), NOT a generic <c>500</c>. Binding fails before any handler
/// runs, so these never touch the database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MalformedRequestTests(CustomWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_account_with_unmapped_enum_type_returns_400_bad_request()
    {
        // "Bogus" is not a member of AccountType — enum binding throws.
        var body = new
        {
            name = "Malformed Type Account",
            type = "Bogus",
            balance = 0m,
            currency = "MDL",
            openingDate = "2026-01-01",
            notes = (string?)null,
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/accounts", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertBadRequestTitleAsync(response);
    }

    [Fact]
    public async Task Post_transaction_with_unmapped_direction_returns_400_bad_request()
    {
        // "Sideways" is not a member of TransactionDirection — enum binding throws.
        var body = new
        {
            accountId = Guid.NewGuid(),
            transactionDate = "2026-01-01",
            direction = "Sideways",
            amount = 10m,
            description = "x",
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/transactions", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertBadRequestTitleAsync(response);
    }

    [Fact]
    public async Task Post_transaction_with_syntactically_broken_json_returns_400_bad_request()
    {
        // Truncated / invalid JSON — System.Text.Json throws during binding.
        using var content = new StringContent(
            "{ \"accountId\": \"not-a-guid\", \"amount\": ",
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response = await _client.PostAsync("/transactions", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertBadRequestTitleAsync(response);
    }

    private static async Task AssertBadRequestTitleAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("title").GetString().Should().Be("Bad Request");
    }
}
