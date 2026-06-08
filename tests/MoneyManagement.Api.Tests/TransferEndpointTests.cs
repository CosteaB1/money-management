using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>POST /transfers</c>: same-currency happy path
/// (creates both legs), cross-currency missing destination amount (400), same
/// source/destination (400), and unknown account (404).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Same_currency_transfer_creates_both_legs()
    {
        Guid source = await _fx.CreateAccountAsync(currency: "MDL", balance: 1000m);
        Guid destination = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);

        var body = new
        {
            sourceAccountId = source,
            destinationAccountId = destination,
            amount = 250m,
            date = "2024-06-01",
            description = _fx.Unique("Transfer"),
            categoryId = (Guid?)null,
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transfers", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        Guid sourceTx = doc.RootElement.GetProperty("sourceTransactionId").GetGuid();
        Guid destTx = doc.RootElement.GetProperty("destinationTransactionId").GetGuid();
        sourceTx.Should().NotBeEmpty();
        destTx.Should().NotBe(sourceTx);

        // Source leg is an Expense flagged as transfer.
        HttpResponseMessage srcList = await Client.GetAsync($"/transactions?accountId={source}");
        using JsonDocument srcDoc = await _fx.ReadDocAsync(srcList);
        JsonElement srcMatch = srcDoc.RootElement.GetProperty("items").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == sourceTx);
        srcMatch.GetProperty("direction").GetString().Should().Be("Expense");
        srcMatch.GetProperty("isTransfer").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Cross_currency_transfer_without_destination_amount_returns_400()
    {
        Guid source = await _fx.CreateAccountAsync(currency: "MDL", balance: 10000m);
        Guid destination = await _fx.CreateAccountAsync(currency: "USD", balance: 0m);

        var body = new
        {
            sourceAccountId = source,
            destinationAccountId = destination,
            amount = 1000m,
            date = "2024-06-01",
            description = _fx.Unique("Transfer"),
            categoryId = (Guid?)null,
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transfers", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transfers.destination_amount_required");
    }

    [Fact]
    public async Task Transfer_with_same_source_and_destination_returns_400()
    {
        Guid account = await _fx.CreateAccountAsync(currency: "MDL", balance: 100m);

        var body = new
        {
            sourceAccountId = account,
            destinationAccountId = account,
            amount = 10m,
            date = "2024-06-01",
            description = _fx.Unique("Transfer"),
            categoryId = (Guid?)null,
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transfers", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transfer.same_source_and_destination");
    }

    [Fact]
    public async Task Transfer_with_unknown_source_returns_404()
    {
        Guid destination = await _fx.CreateAccountAsync(currency: "MDL");

        var body = new
        {
            sourceAccountId = Guid.NewGuid(),
            destinationAccountId = destination,
            amount = 10m,
            date = "2024-06-01",
            description = _fx.Unique("Transfer"),
            categoryId = (Guid?)null,
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transfers", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transfer.source_account_not_found");
    }
}
