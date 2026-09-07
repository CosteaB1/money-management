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

    [Fact]
    public async Task Export_then_import_round_trips_the_pool_tables()
    {
        // The pool tables joined the backup at schema v6. pool_unit_events is the
        // awkward one: it FKs BOTH transactions and pool_participants, and its
        // units / nav_per_unit are numeric(28,12) rather than the 2dp money scale.
        // This drives a full export -> augment -> import -> re-export cycle over a
        // synthetic pool, then restores the captured baseline so the shared
        // money_management_inttest DB is left as found.
        byte[] baseline = await ExportAsync();

        var accountId = Guid.CreateVersion7();
        var transactionId = Guid.CreateVersion7();
        var poolId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var friendId = Guid.CreateVersion7();
        var seedEventId = Guid.CreateVersion7();
        var subscriptionEventId = Guid.CreateVersion7();
        var distributionEventId = Guid.CreateVersion7();

        const string nav = "1.000001234567";
        const string createdAt = "2026-04-01T00:00:00Z";

        try
        {
            JsonNode document = JsonNode.Parse(baseline)!;

            // A dedicated account (pools.account_id is UNIQUE) and one real
            // transaction for the subscription's movement link to point at.
            document["accounts"]!.AsArray().Add(new JsonObject
            {
                ["id"] = accountId,
                ["name"] = "Pool backup round-trip",
                ["type"] = "CryptoExchange",
                ["balanceAmount"] = 0m,
                ["balanceCurrency"] = "USD",
                ["openingDate"] = "2026-04-01",
                ["isArchived"] = false,
                ["notes"] = null,
                ["createdAt"] = createdAt,
                ["updatedAt"] = createdAt,
            });

            document["transactions"]!.AsArray().Add(new JsonObject
            {
                ["id"] = transactionId,
                ["accountId"] = accountId,
                ["categoryId"] = null,
                ["transactionDate"] = "2026-04-10",
                ["direction"] = "Income",
                ["amountValue"] = 1000m,
                ["amountCurrency"] = "USD",
                ["description"] = "Ion subscribes",
                ["notes"] = null,
                ["originalAmount"] = null,
                ["originalCurrency"] = null,
                ["source"] = "Manual",
                ["importBatchId"] = null,
                ["isTransfer"] = false,
                ["counterAccountId"] = null,
                ["isAdjustment"] = false,
                ["isDeleted"] = false,
                ["createdAt"] = createdAt,
                ["updatedAt"] = createdAt,
            });

            document["pools"]!.AsArray().Add(new JsonObject
            {
                ["id"] = poolId,
                ["accountId"] = accountId,
                ["name"] = "Backup pool",
                ["currency"] = "USD",
                ["inceptionDate"] = "2026-04-01",
                ["notes"] = null,
                ["isArchived"] = false,
                ["createdAt"] = createdAt,
                ["updatedAt"] = createdAt,
            });

            JsonArray participants = document["poolParticipants"]!.AsArray();
            participants.Add(Participant(ownerId, poolId, "Me", isOwner: true, createdAt));
            participants.Add(Participant(friendId, poolId, "Ion", isOwner: false, createdAt));

            JsonArray events = document["poolUnitEvents"]!.AsArray();

            // Seed: owner only, no cash leg, no transaction.
            events.Add(UnitEvent(
                seedEventId, poolId, ownerId, "Seed", "2026-04-01",
                units: "1050", navPerUnit: "1", poolValuePreMoney: 0m,
                cashValue: null, cashCurrency: null, settledOn: null,
                movementTransactionId: null, createdAt));

            // Subscription: carries cash AND a movement transaction id.
            events.Add(UnitEvent(
                subscriptionEventId, poolId, friendId, "Subscription", "2026-04-10",
                units: "1000", navPerUnit: "1", poolValuePreMoney: 1050m,
                cashValue: 1000m, cashCurrency: "USD", settledOn: "2026-04-10",
                movementTransactionId: transactionId, createdAt));

            // Distribution: closed but UNPAID - null settledOn with non-null cash,
            // the one shape only a distribution is allowed to have. Also carries
            // the 12-decimal-place NAV.
            events.Add(UnitEvent(
                distributionEventId, poolId, friendId, "Distribution", "2026-04-30",
                units: "50", navPerUnit: nav, poolValuePreMoney: 2100m,
                cashValue: 50.00m, cashCurrency: "USD", settledOn: null,
                movementTransactionId: null, createdAt));

            HttpResponseMessage importResponse =
                await ImportAsync(Encoding.UTF8.GetBytes(document.ToJsonString()));
            importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var counts = JsonDocument.Parse(await importResponse.Content.ReadAsStringAsync()))
            {
                counts.RootElement.GetProperty("pools").GetInt32().Should().Be(1);
                counts.RootElement.GetProperty("poolParticipants").GetInt32().Should().Be(2);
                counts.RootElement.GetProperty("poolUnitEvents").GetInt32().Should().Be(3);
            }

            using var after = JsonDocument.Parse(await ExportAsync());
            JsonElement root = after.RootElement;

            root.GetProperty("schemaVersion").GetInt32().Should().Be(
                6, "the pool tables shipped as schema v6");

            JsonElement pool = Single(root, "pools", poolId);
            pool.GetProperty("accountId").GetGuid().Should().Be(accountId);
            pool.GetProperty("currency").GetString().Should().Be("USD");
            pool.GetProperty("inceptionDate").GetString().Should().Be("2026-04-01");

            Single(root, "poolParticipants", ownerId).GetProperty("isOwner").GetBoolean().Should().BeTrue();
            Single(root, "poolParticipants", friendId).GetProperty("isOwner").GetBoolean().Should().BeFalse();

            JsonElement seed = Single(root, "poolUnitEvents", seedEventId);
            seed.GetProperty("kind").GetString().Should().Be("Seed");
            seed.GetProperty("cashValue").ValueKind.Should().Be(JsonValueKind.Null);
            seed.GetProperty("cashCurrency").ValueKind.Should().Be(JsonValueKind.Null);
            seed.GetProperty("settledOn").ValueKind.Should().Be(JsonValueKind.Null);
            seed.GetProperty("movementTransactionId").ValueKind.Should().Be(JsonValueKind.Null);
            seed.GetProperty("poolValuePreMoney").GetDecimal().Should().Be(0m);

            JsonElement subscription = Single(root, "poolUnitEvents", subscriptionEventId);
            subscription.GetProperty("movementTransactionId").GetGuid().Should().Be(
                transactionId,
                "an event linked to a transaction must be reinserted AFTER transactions");
            subscription.GetProperty("cashValue").GetDecimal().Should().Be(1000m);
            subscription.GetProperty("cashCurrency").GetString().Should().Be("USD");
            subscription.GetProperty("settledOn").GetString().Should().Be("2026-04-10");

            JsonElement distribution = Single(root, "poolUnitEvents", distributionEventId);
            distribution.GetProperty("settledOn").ValueKind.Should().Be(
                JsonValueKind.Null,
                "a closed-but-unpaid distribution is the one cash event with no settlement date");
            distribution.GetProperty("cashValue").GetDecimal().Should().Be(50.00m);
            distribution.GetProperty("movementTransactionId").ValueKind.Should().Be(JsonValueKind.Null);

            // numeric(28,12) survived the DB round-trip: at the repo's usual
            // numeric(18,2) this would come back as 1.00.
            distribution.GetProperty("navPerUnit").GetDecimal().Should().Be(1.000001234567m);
            distribution.GetProperty("navPerUnit").GetRawText().Should().StartWith("1.000001234567");
        }
        finally
        {
            // Full-replace with the captured baseline: the synthetic account,
            // transaction and pool rows go away with it.
            HttpResponseMessage restore = await ImportAsync(baseline);
            restore.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var restored = JsonDocument.Parse(await ExportAsync());
        restored.RootElement.GetProperty("pools").GetArrayLength().Should().Be(
            0, "the baseline restore must leave no pool residue for the other tests in this collection");
    }

    private static JsonObject Participant(Guid id, Guid poolId, string name, bool isOwner, string timestamp) =>
        new()
        {
            ["id"] = id,
            ["poolId"] = poolId,
            ["name"] = name,
            ["isOwner"] = isOwner,
            ["joinedOn"] = "2026-04-01",
            ["isArchived"] = false,
            ["createdAt"] = timestamp,
            ["updatedAt"] = timestamp,
        };

    private static JsonObject UnitEvent(
        Guid id,
        Guid poolId,
        Guid participantId,
        string kind,
        string occurredOn,
        string units,
        string navPerUnit,
        decimal poolValuePreMoney,
        decimal? cashValue,
        string? cashCurrency,
        string? settledOn,
        Guid? movementTransactionId,
        string timestamp) =>
        new()
        {
            ["id"] = id,
            ["poolId"] = poolId,
            ["participantId"] = participantId,
            ["kind"] = kind,
            ["occurredOn"] = occurredOn,
            // Written as raw JSON numbers so the 12-decimal-place NAV is not
            // reshaped by a decimal literal on the way in.
            ["units"] = JsonNode.Parse(units),
            ["navPerUnit"] = JsonNode.Parse(navPerUnit),
            ["poolValuePreMoney"] = poolValuePreMoney,
            ["cashValue"] = cashValue,
            ["cashCurrency"] = cashCurrency,
            ["settledOn"] = settledOn,
            ["movementTransactionId"] = movementTransactionId,
            ["notes"] = null,
            ["createdAt"] = timestamp,
            ["updatedAt"] = timestamp,
        };

    private static JsonElement Single(JsonElement root, string table, Guid id)
    {
        foreach (JsonElement row in root.GetProperty(table).EnumerateArray())
        {
            if (row.GetProperty("id").GetGuid() == id)
            {
                return row;
            }
        }

        throw new InvalidOperationException($"No row with id '{id}' in '{table}'.");
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
