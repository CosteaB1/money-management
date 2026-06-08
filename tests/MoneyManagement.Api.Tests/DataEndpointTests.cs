using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MoneyManagement.Api.Tests.Support;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// NON-DESTRUCTIVE endpoint coverage for <c>/data</c>: the export shape and the
/// import request-shape / malformed-payload guards. The full destructive
/// export→import restore round-trip lives in <see cref="BackupRoundTripTests"/>;
/// the cases here all reject before any restore runs, so they never wipe the
/// inttest DB and are safe alongside the data-mutating endpoint tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DataEndpointTests(CustomWebApplicationFactory factory)
{
    private readonly ApiTestFixture _fx = new(factory);
    private HttpClient Client => _fx.Client;

    [Fact]
    public async Task Export_returns_json_attachment_with_schema_version()
    {
        HttpResponseMessage response = await Client.GetAsync("/data/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("schemaVersion", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("categoryPatterns", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("accounts", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Import_without_multipart_returns_400()
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_without_file_returns_400()
    {
        using var form = new MultipartFormDataContent { { new StringContent("x"), "notafile" } };
        HttpResponseMessage response = await Client.PostAsync("/data/import", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_with_malformed_json_returns_400_malformed_backup()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("{ not valid json"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", "backup.json");

        HttpResponseMessage response = await Client.PostAsync("/data/import", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("data.malformed_backup");
    }

    [Fact]
    public async Task Import_with_json_null_returns_400_malformed_backup()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("null"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", "backup.json");

        HttpResponseMessage response = await Client.PostAsync("/data/import", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("data.malformed_backup");
    }
}
