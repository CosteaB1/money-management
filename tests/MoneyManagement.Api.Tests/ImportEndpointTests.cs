using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/imports</c>: the <c>/parse</c> request-shape guards
/// (no body / no file / non-PDF / missing accountId) and <c>/commit</c> happy
/// path plus error paths (unknown account 404, empty batch 400, duplicate
/// dedup). <c>/parse</c> with a real PDF is NOT exercised here — that needs a
/// fixture PDF + the parser; the guards covered are the parser-free branches.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ImportEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    private static readonly Guid GroceriesExpenseId = new("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Parse_without_multipart_returns_400()
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await Client.PostAsync("/imports/parse", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_without_file_returns_400()
    {
        using var form = new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "accountId" } };
        HttpResponseMessage response = await Client.PostAsync("/imports/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_with_non_pdf_file_returns_400()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("not a pdf"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "statement.txt");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "accountId");

        HttpResponseMessage response = await Client.PostAsync("/imports/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_with_pdf_but_missing_accountId_returns_400()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4 fake"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "statement.pdf");

        HttpResponseMessage response = await Client.PostAsync("/imports/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_with_maib_pdf_returns_preview()
    {
        // End-to-end parse: upload the synthetic maib fixture so the endpoint streams
        // the file into the ParseStatementCommand and the parser produces a
        // preview (the happy-path file-copy + handler-dispatch branch).
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);

        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maib_sample.pdf");
        byte[] pdfBytes = await File.ReadAllBytesAsync(fixturePath);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "maib_sample.pdf");
        form.Add(new StringContent(accountId.ToString()), "accountId");

        HttpResponseMessage response = await Client.PostAsync("/imports/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("bankSource").GetString().Should().Be("Maib");
        doc.RootElement.GetProperty("fileHash").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("transactions").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Commit_happy_path_creates_batch_and_transactions()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        string desc = _fx.Unique("Imported");

        var body = new
        {
            accountId,
            fileName = "statement.pdf",
            fileHash = Guid.NewGuid().ToString("N"),
            bankSource = "Maib",
            transactions = new[]
            {
                new { transactionDate = "2024-05-01", direction = "Expense", amount = 33.33m, description = desc, categoryId = (Guid?)GroceriesExpenseId },
            },
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/imports/commit", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("importedCount").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("skippedDuplicates").GetInt32().Should().Be(0);

        // Confirm the transaction landed and is tagged as Imported.
        HttpResponseMessage list = await Client.GetAsync($"/transactions?accountId={accountId}");
        using JsonDocument listDoc = await _fx.ReadDocAsync(list);
        JsonElement match = listDoc.RootElement.GetProperty("items").EnumerateArray()
            .Single(t => t.GetProperty("description").GetString() == desc);
        match.GetProperty("source").GetString().Should().Be("Imported");
    }

    [Fact]
    public async Task Commit_duplicate_rows_on_reimport_are_skipped()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        string desc = _fx.Unique("Dup");
        object Body() => new
        {
            accountId,
            fileName = "statement.pdf",
            fileHash = Guid.NewGuid().ToString("N"),
            bankSource = "Maib",
            transactions = new[]
            {
                new { transactionDate = "2024-05-02", direction = "Expense", amount = 9.99m, description = desc, categoryId = (Guid?)null },
            },
        };

        HttpResponseMessage first = await Client.PostAsJsonAsync("/imports/commit", Body());
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage second = await Client.PostAsJsonAsync("/imports/commit", Body());
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument doc = await _fx.ReadDocAsync(second);
        doc.RootElement.GetProperty("skippedDuplicates").GetInt32().Should().Be(1, "the identical row is a duplicate on re-import");
        doc.RootElement.GetProperty("importedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Commit_for_unknown_account_returns_404()
    {
        var body = new
        {
            accountId = Guid.NewGuid(),
            fileName = "statement.pdf",
            fileHash = Guid.NewGuid().ToString("N"),
            bankSource = "Maib",
            transactions = new[]
            {
                new { transactionDate = "2024-05-01", direction = "Expense", amount = 1m, description = "x", categoryId = (Guid?)null },
            },
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/imports/commit", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("account.not_found");
    }

    [Fact]
    public async Task Commit_with_empty_transactions_returns_400()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        var body = new
        {
            accountId,
            fileName = "statement.pdf",
            fileHash = Guid.NewGuid().ToString("N"),
            bankSource = "Maib",
            transactions = Array.Empty<object>(),
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/imports/commit", body);

        // The CommitImportCommandValidator's Transactions.NotEmpty() rule fires
        // before the handler, so the surfaced code is the property name (the
        // handler's "imports.empty_batch" guard is unreachable through the API).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("Transactions");
    }

    [Fact]
    public async Task Commit_learns_a_new_category_pattern()
    {
        Guid accountId = await _fx.CreateAccountAsync(currency: "MDL", balance: 0m);
        string keyword = $"LEARN{Guid.NewGuid():N}";

        var body = new
        {
            accountId,
            fileName = "statement.pdf",
            fileHash = Guid.NewGuid().ToString("N"),
            bankSource = "Maib",
            transactions = new[]
            {
                new { transactionDate = "2024-05-03", direction = "Expense", amount = 5m, description = _fx.Unique("Tx"), categoryId = (Guid?)GroceriesExpenseId },
            },
            learnedPatterns = new[] { new { keyword, categoryId = GroceriesExpenseId } },
        };

        HttpResponseMessage response = await Client.PostAsJsonAsync("/imports/commit", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage patterns = await Client.GetAsync("/category-patterns");
        using JsonDocument doc = await _fx.ReadDocAsync(patterns);
        doc.RootElement.EnumerateArray().Any(p => p.GetProperty("keyword").GetString() == keyword.ToUpperInvariant())
            .Should().BeTrue("the import should have learned the new keyword pattern");
    }
}
