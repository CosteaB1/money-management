using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// HIGH-VALUE guard over <c>EfBackupStore</c> — the scariest previously-untested
/// code. Pins the 2026-05-29 data-loss regression: a backup export → import
/// round-trip must preserve <c>category_patterns</c> (a CASCADE child of
/// categories that a naive restore silently wiped). Also pins the schema-version
/// guard. This test is destructive to <c>money_management_inttest</c> (the restore
/// is a full replace), which is fine — only this dedicated DB is affected.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BackupRoundTripTests(CustomWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Export_then_import_preserves_category_patterns_count()
    {
        // The seeders (CategorySeeder + CategoryPatternSeeder hosted services)
        // populate category_patterns on first boot, so the export carries some.
        byte[] exported = await ExportAsync();

        int patternsInExport;
        using (var doc = JsonDocument.Parse(exported))
        {
            patternsInExport = doc.RootElement.GetProperty("categoryPatterns").GetArrayLength();
        }

        patternsInExport.Should().BeGreaterThan(
            0,
            "the pattern seeder should have populated category_patterns; an empty export can't prove preservation");

        // Re-upload the EXACT exported document. The response reports the per-table
        // row counts the restore actually inserted.
        HttpResponseMessage importResponse = await ImportAsync(exported);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result = JsonDocument.Parse(await importResponse.Content.ReadAsStringAsync());
        result.RootElement.GetProperty("categoryPatterns").GetInt32().Should().Be(
            patternsInExport,
            "the restore must reinstate every category_patterns row (the 2026-05-29 data-loss regression)");

        // And confirm via a fresh export that the count survived the round-trip in the DB.
        byte[] reExported = await ExportAsync();
        using var after = JsonDocument.Parse(reExported);
        after.RootElement.GetProperty("categoryPatterns").GetArrayLength().Should().Be(patternsInExport);
    }

    [Fact]
    public async Task Import_with_unsupported_schema_version_returns_400()
    {
        byte[] exported = await ExportAsync();

        // Bump schemaVersion to an unsupported value, keeping the rest intact.
        JsonNode node = JsonNode.Parse(exported)!;
        node["schemaVersion"] = 999;
        byte[] bumped = Encoding.UTF8.GetBytes(node.ToJsonString());

        HttpResponseMessage response = await ImportAsync(bumped);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be("data.unsupported_schema_version");
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("data.unsupported_schema_version");
    }

    private async Task<byte[]> ExportAsync()
    {
        HttpResponseMessage response = await _client.GetAsync("/data/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<HttpResponseMessage> ImportAsync(byte[] documentBytes)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(documentBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(fileContent, "file", "backup.json");

        return await _client.PostAsync("/data/import", form);
    }
}
