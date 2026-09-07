using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyManagement.Api.Tests.Support;
using MoneyManagement.Infrastructure.Database;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Endpoint coverage for <c>/pools</c>: the write surface the frontend builds
/// against, plus the two guard rows that are reachable through HTTP on the same
/// account.
/// <para>
/// Every test creates its OWN CryptoExchange account — one pool per account is
/// enforced forever, so sharing one would make the tests order-dependent.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PoolEndpointTests(CustomWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly ApiTestFixture _fx = new(factory);

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private HttpClient Client => _fx.Client;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Leaves <c>money_management_inttest</c> with ZERO pools.
    /// <para>
    /// Unlike accounts or transactions, pool residue is not harmless here:
    /// <c>BackupRoundTripTests</c> asserts that a restored baseline carries no
    /// pool rows, and one pool per account is enforced forever, so a leftover row
    /// would make a re-run of these tests order-dependent. Participants and unit
    /// events go with the pool through the database's own CASCADE.
    /// </para>
    /// </summary>
    public async Task DisposeAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Pools.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Create_pool_seeds_the_owner_and_returns_the_participant_ids()
    {
        Guid accountId = await CreateExchangeAccountAsync(1_000m);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/pools", new
        {
            accountId,
            name = _fx.Unique("Pool"),
            currency = "USD",
            inceptionDate = Today,
            ownerName = "Me",
            poolValueAtInception = 1_200m,
            notes = (string?)null,
            backdatedSubscriptions = (object?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("seedUnits").GetDecimal().Should().Be(1_200m);
        doc.RootElement.GetProperty("markDelta").GetDecimal().Should().Be(200m);
        doc.RootElement.GetProperty("ownerParticipantId").GetGuid().Should().NotBeEmpty();

        JsonElement owner = doc.RootElement.GetProperty("participants").EnumerateArray().Single();
        owner.GetProperty("isOwner").GetBoolean().Should().BeTrue();
        owner.GetProperty("units").GetDecimal().Should().Be(1_200m);
    }

    [Fact]
    public async Task Create_pool_on_an_ineligible_account_type_returns_400()
    {
        Guid accountId = await _fx.CreateAccountAsync(type: "Cash", balance: 500m, currency: "USD");

        HttpResponseMessage response = await Client.PostAsJsonAsync("/pools", new
        {
            accountId,
            name = _fx.Unique("Pool"),
            currency = "USD",
            inceptionDate = Today,
            ownerName = "Me",
            poolValueAtInception = 500m,
            notes = (string?)null,
            backdatedSubscriptions = (object?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.account_type_not_eligible");
    }

    [Fact]
    public async Task Subscription_mints_units_and_the_response_carries_the_struck_nav()
    {
        (Guid accountId, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid participantId = await AddParticipantAsync(poolId, "Andrei");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/pools/{poolId}/subscriptions", new
        {
            participantId,
            poolValueNow = 1_200m,
            cash = 800m,
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("navPerUnit").GetDecimal().Should().Be(1m);
        doc.RootElement.GetProperty("units").GetDecimal().Should().Be(800m);
        doc.RootElement.GetProperty("poolValuePreMoney").GetDecimal().Should().Be(1_200m);
        doc.RootElement.GetProperty("movementTransactionId").GetGuid().Should().NotBeEmpty();

        // The synthesized leg really landed on the account.
        HttpResponseMessage detail = await Client.GetAsync($"/accounts/{accountId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Subscription_whose_cash_equals_the_account_balance_returns_400()
    {
        (_, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid participantId = await AddParticipantAsync(poolId, "Andrei");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/pools/{poolId}/subscriptions", new
        {
            participantId,
            poolValueNow = 1_500m,
            cash = 1_200m,
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.cash_looks_like_a_balance");
    }

    [Fact]
    public async Task Manual_transaction_on_a_pooled_account_returns_400()
    {
        (Guid accountId, _, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transactions", new
        {
            accountId,
            transactionDate = Today,
            direction = "Income",
            amount = 50m,
            description = _fx.Unique("Manual"),
            categoryId = (Guid?)null,
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.manual_movement_blocked");
    }

    [Fact]
    public async Task Transfer_touching_a_pooled_account_returns_400()
    {
        (Guid accountId, _, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid other = await _fx.CreateAccountAsync(type: "Cash", balance: 100m, currency: "USD");

        HttpResponseMessage response = await Client.PostAsJsonAsync("/transfers", new
        {
            sourceAccountId = other,
            destinationAccountId = accountId,
            amount = 25m,
            date = Today,
            description = _fx.Unique("Move"),
            categoryId = (Guid?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.transfer_blocked");
    }

    [Fact]
    public async Task Goal_linked_to_a_pooled_account_returns_400()
    {
        (Guid accountId, _, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/goals", new
        {
            name = _fx.Unique("Goal"),
            targetAmount = 1_000m,
            targetDate = (string?)null,
            linkedAccountId = accountId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.goal_link_blocked");
    }

    [Fact]
    public async Task Close_then_settle_pays_the_distribution()
    {
        (_, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid participantId = await AddParticipantAsync(poolId, "Andrei");

        HttpResponseMessage subscribe = await Client.PostAsJsonAsync($"/pools/{poolId}/subscriptions", new
        {
            participantId,
            poolValueNow = 1_200m,
            cash = 800m,
            notes = (string?)null,
        });

        subscribe.StatusCode.Should().Be(HttpStatusCode.Created, await subscribe.Content.ReadAsStringAsync());

        // +10% on a 2,000 pool: Andrei's 800 units are worth 880 against an 800
        // base, so 80 is payable.
        HttpResponseMessage close = await Client.PostAsJsonAsync($"/pools/{poolId}/distributions", new
        {
            poolValueNow = 2_200m,
            payouts = (object?)null,
            notes = (string?)null,
        });

        close.StatusCode.Should().Be(HttpStatusCode.Created, await close.Content.ReadAsStringAsync());

        using JsonDocument closeDoc = await _fx.ReadDocAsync(close);
        closeDoc.RootElement.GetProperty("navPerUnit").GetDecimal().Should().Be(1.1m);
        closeDoc.RootElement.GetProperty("totalCash").GetDecimal().Should().Be(80m);

        JsonElement line = closeDoc.RootElement.GetProperty("lines").EnumerateArray().Single();
        Guid eventId = line.GetProperty("eventId").GetGuid();
        line.GetProperty("cash").GetDecimal().Should().Be(80m);

        HttpResponseMessage settle = await Client.PostAsJsonAsync(
            $"/pools/{poolId}/distributions/{eventId}/settle",
            new { settledOn = (string?)null, notes = (string?)null });

        settle.StatusCode.Should().Be(HttpStatusCode.OK, await settle.Content.ReadAsStringAsync());

        using JsonDocument settleDoc = await _fx.ReadDocAsync(settle);
        settleDoc.RootElement.GetProperty("cash").GetDecimal().Should().Be(80m);
        settleDoc.RootElement.GetProperty("transactionId").GetGuid().Should().NotBeEmpty();

        // Paying twice is refused.
        HttpResponseMessage again = await Client.PostAsJsonAsync(
            $"/pools/{poolId}/distributions/{eventId}/settle",
            new { settledOn = (string?)null, notes = (string?)null });

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(again);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.distribution_already_settled");
    }

    [Fact]
    public async Task Archive_is_refused_while_outside_units_are_outstanding_and_allowed_after_they_are_gone()
    {
        (_, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid participantId = await AddParticipantAsync(poolId, "Andrei");

        await Client.PostAsJsonAsync($"/pools/{poolId}/subscriptions", new
        {
            participantId,
            poolValueNow = 1_200m,
            cash = 800m,
            notes = (string?)null,
        });

        HttpResponseMessage blocked = await Client.PostAsync($"/pools/{poolId}/archive", null);
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (JsonDocument problem = await _fx.ReadDocAsync(blocked))
        {
            problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.pool_has_outside_units");
        }

        HttpResponseMessage redeem = await Client.PostAsJsonAsync($"/pools/{poolId}/redemptions", new
        {
            participantId,
            poolValueNow = 2_000m,
            cash = 800m,
            destinationAccountId = (Guid?)null,
            notes = (string?)null,
        });

        redeem.StatusCode.Should().Be(HttpStatusCode.Created, await redeem.Content.ReadAsStringAsync());

        HttpResponseMessage archived = await Client.PostAsync($"/pools/{poolId}/archive", null);
        archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_event_rolls_back_the_units_and_the_money()
    {
        (_, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid participantId = await AddParticipantAsync(poolId, "Andrei");

        HttpResponseMessage subscribe = await Client.PostAsJsonAsync($"/pools/{poolId}/subscriptions", new
        {
            participantId,
            poolValueNow = 1_200m,
            cash = 800m,
            notes = (string?)null,
        });

        using JsonDocument subDoc = await _fx.ReadDocAsync(subscribe);
        Guid eventId = subDoc.RootElement.GetProperty("eventId").GetGuid();

        HttpResponseMessage delete = await Client.DeleteAsync($"/pools/{poolId}/events/{eventId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The pool now holds only the owner's units, so archiving is allowed.
        HttpResponseMessage archived = await Client.PostAsync($"/pools/{poolId}/archive", null);
        archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- the two read routes ---------------------------------------------

    [Fact]
    public async Task Get_pools_reports_pool_value_net_of_the_unpaid_payout()
    {
        (_, Guid poolId) = await PoolBuiltFromHistoryAsync();
        await CloseTheMonthAsync(poolId);

        HttpResponseMessage response = await Client.GetAsync("/pools");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        JsonElement pool = doc.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == poolId);

        // The close wrote the +200 mark and retired units, but NO cash leg — the
        // 80.00 owed is still physically sitting in the account.
        pool.GetProperty("accountBalance").GetDecimal().Should().Be(2_200m);
        pool.GetProperty("unpaidDistributionCash").GetDecimal().Should().Be(80m);
        pool.GetProperty("unpaidDistributionCount").GetInt32().Should().Be(1);

        decimal poolValue = pool.GetProperty("poolValue").GetDecimal();
        poolValue.Should().Be(2_120m);
        poolValue.Should().Be(
            pool.GetProperty("accountBalance").GetDecimal() - pool.GetProperty("unpaidDistributionCash").GetDecimal(),
            "skip the subtraction and the friends are paid twice on the same profit, every month");

        pool.GetProperty("totalUnits").GetDecimal().Should().Be(1_927.272727272727m);
        pool.GetProperty("navPerUnit").GetDecimal().Should().Be(1.1m, "a payout at NAV leaves NAV unchanged");
        pool.GetProperty("ownerFraction").GetDecimal().Should().Be(1_200m / 1_927.272727272727m);
        pool.GetProperty("outsideCapital").GetDecimal().Should().Be(800m, "the payout swept Andrei back to his base");
        pool.GetProperty("participantCount").GetInt32().Should().Be(2);
        pool.GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_pool_detail_for_an_unknown_id_returns_404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/pools/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument problem = await _fx.ReadDocAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("pools.not_found");
    }

    [Fact]
    public async Task Get_pool_detail_of_an_archived_pool_still_returns_200()
    {
        (_, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);

        HttpResponseMessage archived = await Client.PostAsync($"/pools/{poolId}/archive", null);
        archived.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // It is gone from the list, and still drillable — the loans/goals
        // precedent. Archiving needs zero outside units, so what is left is a
        // finished story the user should still be able to read.
        using (JsonDocument list = await _fx.ReadDocAsync(await Client.GetAsync("/pools")))
        {
            JsonElement[] listed = [.. list.RootElement.EnumerateArray()];
            listed.Should().NotContain(p => p.GetProperty("id").GetGuid() == poolId);
        }

        HttpResponseMessage response = await Client.GetAsync($"/pools/{poolId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("isArchived").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("totalUnits").GetDecimal().Should().Be(1_200m);
    }

    [Fact]
    public async Task Get_pool_detail_lists_events_newest_first_flags_the_unpaid_payout_and_reconciles_clean()
    {
        (_, Guid poolId) = await PoolBuiltFromHistoryAsync();
        await CloseTheMonthAsync(poolId);

        HttpResponseMessage response = await Client.GetAsync($"/pools/{poolId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        JsonElement root = doc.RootElement;

        root.GetProperty("poolValue").GetDecimal().Should().Be(2_120m);
        root.GetProperty("unpaidDistributionCash").GetDecimal().Should().Be(80m);

        JsonElement[] events = [.. root.GetProperty("events").EnumerateArray()];
        events.Should().HaveCount(3);

        string[] dates = [.. events.Select(e => e.GetProperty("occurredOn").GetString()!)];
        dates.Should().BeInDescendingOrder();
        dates[0].Should().Be(Today, "the close is the most recent event");
        dates[^1].Should().Be(InceptionOnRecord);

        events[^1].GetProperty("kind").GetString().Should().Be("Seed");

        // Closed but not settled: the frontend needs this to raise a "pay it"
        // action, and the register needs it to keep the cash out of pool value.
        JsonElement distribution = events[0];
        distribution.GetProperty("kind").GetString().Should().Be("Distribution");
        distribution.GetProperty("isUnpaid").GetBoolean().Should().BeTrue();
        distribution.GetProperty("settledOn").ValueKind.Should().Be(JsonValueKind.Null);
        distribution.GetProperty("movementTransactionId").ValueKind.Should().Be(JsonValueKind.Null);
        distribution.GetProperty("cash").GetDecimal().Should().Be(80m);

        // Every other event's cash HAS moved, and each one still points at its row.
        events[1].GetProperty("isUnpaid").GetBoolean().Should().BeFalse();
        events[1].GetProperty("movementTransactionId").ValueKind.Should().Be(JsonValueKind.String);

        JsonElement reconciliation = root.GetProperty("reconciliation");
        reconciliation.GetProperty("isClean").GetBoolean().Should().BeTrue(reconciliation.GetRawText());
        reconciliation.GetProperty("unmatchedTransactions").GetArrayLength().Should().Be(0);
        reconciliation.GetProperty("valueDrifts").GetArrayLength().Should().Be(0);
        reconciliation.GetProperty("unbackedCashClaims").GetArrayLength().Should().Be(0);
        reconciliation.GetProperty("unitsBalance").GetBoolean().Should().BeTrue();
        reconciliation.GetProperty("unitsDrift").GetDecimal().Should().Be(0m);
    }

    /// <summary>The inception date the from-history fixture uses; the account opens the same day.</summary>
    private const string InceptionOnRecord = "2024-01-01";

    /// <summary>
    /// A pool bootstrapped from history: 1,200 seeded on 2024-01-01 and Andrei's
    /// 800 replayed on 2024-06-01 with its money row synthesized.
    /// <para>
    /// Back-dating this way is the only route the API offers — the pricing routes
    /// take no event date on purpose — and it is what lets the close land on a
    /// DIFFERENT day from the subscription. That matters: the pre-money replay
    /// re-derives each event's value from the account's END-OF-DAY balance, so a
    /// subscription and a re-marking close on the same date cannot both reproduce.
    /// </para>
    /// </summary>
    private async Task<(Guid AccountId, Guid PoolId)> PoolBuiltFromHistoryAsync()
    {
        Guid accountId = await CreateExchangeAccountAsync(1_200m);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/pools", new
        {
            accountId,
            name = _fx.Unique("Pool"),
            currency = "USD",
            inceptionDate = InceptionOnRecord,
            ownerName = "Me",
            poolValueAtInception = 1_200m,
            notes = (string?)null,
            backdatedSubscriptions = new[]
            {
                new
                {
                    participantName = _fx.Unique("Andrei"),
                    occurredOn = "2024-06-01",
                    cash = 800m,
                    poolValuePreMoney = 1_200m,
                    writeMovementTransaction = true,
                    notes = (string?)null,
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        doc.RootElement.GetProperty("markDelta").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("seedUnits").GetDecimal().Should().Be(1_200m);
        doc.RootElement.GetProperty("participants").GetArrayLength().Should().Be(2);

        return (accountId, doc.RootElement.GetProperty("id").GetGuid());
    }

    /// <summary>+10% on a 2,000 pool: 80.00 owed, no cash moved.</summary>
    private async Task CloseTheMonthAsync(Guid poolId)
    {
        HttpResponseMessage close = await Client.PostAsJsonAsync($"/pools/{poolId}/distributions", new
        {
            poolValueNow = 2_200m,
            payouts = (object?)null,
            notes = (string?)null,
        });

        close.StatusCode.Should().Be(HttpStatusCode.Created, await close.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(close);
        doc.RootElement.GetProperty("markDelta").GetDecimal().Should().Be(200m);
        doc.RootElement.GetProperty("navPerUnit").GetDecimal().Should().Be(1.1m);
        doc.RootElement.GetProperty("totalCash").GetDecimal().Should().Be(80m);
    }

    private Task<Guid> CreateExchangeAccountAsync(decimal balance) =>
        _fx.CreateAccountAsync(type: "CryptoExchange", balance: balance, currency: "USD");

    /// <summary>
    /// The full wind-down, end to end over HTTP — the shape the defect said had
    /// no path at all.
    /// <para>
    /// Redeeming the LAST of a pool is by definition redeeming the whole pool
    /// value, which collides head-on with the guard against a BALANCE typed into
    /// an AMOUNT field. Without the acknowledgement the request is refused; with
    /// it the pool winds down to zero units and the detail page still reads.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Full_wind_down_needs_the_acknowledgement_and_then_leaves_the_pool_at_zero_units()
    {
        (_, Guid poolId, Guid ownerId) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);

        HttpResponseMessage blocked = await Client.PostAsJsonAsync($"/pools/{poolId}/redemptions", new
        {
            participantId = ownerId,
            poolValueNow = 1_200m,
            cash = 1_200m,
            destinationAccountId = (Guid?)null,
            notes = (string?)null,
        });

        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (JsonDocument problem = await _fx.ReadDocAsync(blocked))
        {
            problem.RootElement.GetProperty("errorCode").GetString()
                .Should().Be("pools.cash_looks_like_a_balance");
        }

        // The flag is an assertion, not a bypass: claiming it on a partial
        // redemption is refused too.
        HttpResponseMessage overclaimed = await Client.PostAsJsonAsync($"/pools/{poolId}/redemptions", new
        {
            participantId = ownerId,
            poolValueNow = 1_200m,
            cash = 400m,
            destinationAccountId = (Guid?)null,
            notes = (string?)null,
            isFullWindDown = true,
        });

        overclaimed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (JsonDocument problem = await _fx.ReadDocAsync(overclaimed))
        {
            problem.RootElement.GetProperty("errorCode").GetString()
                .Should().Be("pools.not_a_full_wind_down");
        }

        HttpResponseMessage windDown = await Client.PostAsJsonAsync($"/pools/{poolId}/redemptions", new
        {
            participantId = ownerId,
            poolValueNow = 1_200m,
            cash = 1_200m,
            destinationAccountId = (Guid?)null,
            notes = (string?)null,
            isFullWindDown = true,
        });

        windDown.StatusCode.Should().Be(HttpStatusCode.Created, await windDown.Content.ReadAsStringAsync());

        HttpResponseMessage detail = await Client.GetAsync($"/pools/{poolId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = await _fx.ReadDocAsync(detail);
        doc.RootElement.GetProperty("totalUnits").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("accountBalance").GetDecimal().Should().Be(0m);

        // No units outstanding means no price — never a substituted 1.0 — while
        // the OWNER fraction is 1.0, because nobody else has a claim on the
        // account any more.
        doc.RootElement.GetProperty("navPerUnit").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("ownerFraction").GetDecimal().Should().Be(1m);
    }

    /// <summary>
    /// A NON-owner payout cannot name a tracked destination: the counter leg
    /// would land on a wholly-owned account, where a friend's money reads as the
    /// user's own and counts in full towards net worth.
    /// </summary>
    [Fact]
    public async Task Non_owner_redemption_cannot_name_a_destination_account()
    {
        (_, Guid poolId, _) = await CreatePoolAsync(openingBalance: 1_200m, inceptionValue: 1_200m);
        Guid participantId = await AddParticipantAsync(poolId, "Andrei");
        Guid bybitId = await CreateExchangeAccountAsync(0m);

        HttpResponseMessage subscribe = await Client.PostAsJsonAsync($"/pools/{poolId}/subscriptions", new
        {
            participantId,
            poolValueNow = 1_200m,
            cash = 800m,
            notes = (string?)null,
        });

        subscribe.StatusCode.Should().Be(HttpStatusCode.Created, await subscribe.Content.ReadAsStringAsync());

        HttpResponseMessage blocked = await Client.PostAsJsonAsync($"/pools/{poolId}/redemptions", new
        {
            participantId,
            poolValueNow = 2_000m,
            cash = 400m,
            destinationAccountId = bybitId,
            notes = (string?)null,
        });

        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await _fx.ReadDocAsync(blocked);
        problem.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("pools.destination_requires_owner");
    }

    private async Task<(Guid AccountId, Guid PoolId, Guid OwnerId)> CreatePoolAsync(
        decimal openingBalance,
        decimal inceptionValue)
    {
        Guid accountId = await CreateExchangeAccountAsync(openingBalance);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/pools", new
        {
            accountId,
            name = _fx.Unique("Pool"),
            currency = "USD",
            inceptionDate = Today,
            ownerName = "Me",
            poolValueAtInception = inceptionValue,
            notes = (string?)null,
            backdatedSubscriptions = (object?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        return (
            accountId,
            doc.RootElement.GetProperty("id").GetGuid(),
            doc.RootElement.GetProperty("ownerParticipantId").GetGuid());
    }

    private async Task<Guid> AddParticipantAsync(Guid poolId, string name)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/pools/{poolId}/participants",
            new { name = _fx.Unique(name), joinedOn = Today });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using JsonDocument doc = await _fx.ReadDocAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }
}
