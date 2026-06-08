using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/transactions</c>: create (happy + the cross-entity
/// error paths the validator can't catch), list with filters + paging, update
/// category (incl. flow-mismatch 422-as-400), update notes, and delete.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransactionEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    // Seeded category ids (CategorySeeder) — stable reference data.
    private static readonly Guid GroceriesExpenseId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SalaryIncomeId = new("00000000-0000-0000-0000-000000000007");

    [Fact]
    public async Task Create_transaction_happy_path_persists_and_lists()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        Guid txId = await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 42.50m, categoryId: GroceriesExpenseId);

        HttpResponseMessage list = await Client.GetAsync($"/transactions?accountId={accountId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement items = doc.RootElement.GetProperty("items");
        JsonElement match = items.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == txId);
        match.GetProperty("amount").GetDecimal().Should().Be(42.50m);
        match.GetProperty("direction").GetString().Should().Be("Expense");
        match.GetProperty("categoryId").GetGuid().Should().Be(GroceriesExpenseId);
        doc.RootElement.GetProperty("pageNumber").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Create_transaction_for_unknown_account_returns_404()
    {
        var body = new { accountId = Guid.NewGuid(), transactionDate = "2024-06-01", direction = "Expense", amount = 10m, description = "x" };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transactions", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Create_transaction_with_unknown_category_returns_404()
    {
        Guid accountId = await _fx.CreateAccountAsync();
        var body = new { accountId, transactionDate = "2024-06-01", direction = "Expense", amount = 10m, description = "x", categoryId = Guid.NewGuid() };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transactions", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("category.not_found");
    }

    [Fact]
    public async Task Create_transaction_with_currency_mismatch_returns_400()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL");
        var body = new { accountId, transactionDate = "2024-06-01", direction = "Expense", amount = 10m, description = "x", currency = "USD" };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transactions", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transaction.currency_mismatch_account");
    }

    [Fact]
    public async Task Update_transaction_category_to_flow_mismatch_returns_400()
    {
        Guid accountId = await _fx.CreateAccountAsync();
        // Expense transaction; assigning an Income-only category violates flow.
        Guid txId = await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 10m);

        var body = new { categoryId = SalaryIncomeId };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/transactions/{txId}/category", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transaction.category_flow_mismatch");
    }

    [Fact]
    public async Task Update_transaction_category_happy_path_returns_204()
    {
        Guid accountId = await _fx.CreateAccountAsync();
        Guid txId = await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 10m);

        var body = new { categoryId = GroceriesExpenseId };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/transactions/{txId}/category", body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync($"/transactions?accountId={accountId}");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.GetProperty("items").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == txId);
        match.GetProperty("categoryId").GetGuid().Should().Be(GroceriesExpenseId);
    }

    [Fact]
    public async Task Update_transaction_category_on_unknown_id_returns_404()
    {
        var body = new { categoryId = GroceriesExpenseId };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/transactions/{Guid.NewGuid()}/category", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("transaction.not_found");
    }

    [Fact]
    public async Task Update_transaction_notes_happy_path_persists()
    {
        Guid accountId = await _fx.CreateAccountAsync();
        Guid txId = await _fx.CreateTransactionAsync(accountId);

        var body = new { notes = "annotated note" };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/transactions/{txId}/notes", body);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync($"/transactions?accountId={accountId}");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        JsonElement match = doc.RootElement.GetProperty("items").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == txId);
        match.GetProperty("notes").GetString().Should().Be("annotated note");
    }

    [Fact]
    public async Task Update_transaction_notes_on_unknown_id_returns_404()
    {
        var body = new { notes = "x" };
        HttpResponseMessage response = await Client.PutAsJsonAsync($"/transactions/{Guid.NewGuid()}/notes", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_transaction_happy_path_then_gone_from_list()
    {
        Guid accountId = await _fx.CreateAccountAsync();
        Guid txId = await _fx.CreateTransactionAsync(accountId);

        HttpResponseMessage del = await Client.DeleteAsync($"/transactions/{txId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await Client.GetAsync($"/transactions?accountId={accountId}");
        using JsonDocument doc = await _fx.ReadDocAsync(list);
        doc.RootElement.GetProperty("items").EnumerateArray()
            .Any(t => t.GetProperty("id").GetGuid() == txId).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_unknown_transaction_returns_404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/transactions/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_transactions_filters_by_direction_and_date_range()
    {
        Guid accountId = await _fx.CreateAccountAsync();
        await _fx.CreateTransactionAsync(accountId, direction: "Expense", amount: 5m, transactionDate: "2024-03-10");
        Guid incomeId = await _fx.CreateTransactionAsync(accountId, direction: "Income", amount: 7m, transactionDate: "2024-03-15", categoryId: SalaryIncomeId);

        HttpResponseMessage response = await Client.GetAsync(
            $"/transactions?accountId={accountId}&direction=Income&from=2024-03-01&to=2024-03-31");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        JsonElement[] items = [.. doc.RootElement.GetProperty("items").EnumerateArray()];
        items.Should().OnlyContain(t => t.GetProperty("direction").GetString() == "Income");
        items.Should().Contain(t => t.GetProperty("id").GetGuid() == incomeId);
    }
}
