using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the six guarded event endpoints (list/get/create/update/delete/close) via
/// WebApplicationFactory (real MariaDB/Redis - skippable). Covers create + the full GET DTO (fields +
/// derived expenseCount, no embedded expense list, OQ15), the summary list + start_date DESC sort +
/// the ?closed filter (OQ10), update info, the one-way close (re-close/update/delete after close →
/// 400 code 9001; the event stays closed), OPEN-only hard delete + subsequent 404, the resource-owned
/// 404 (code 9000, never 403) on every route for another user's event, the anonymous 401, and the
/// endDate&lt;startDate validation (400 code 1001 with camelCase error.fields). Assertions target
/// stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class EventsEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<JsonElement[]> ListEventsAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"api/v1/events{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    [SkippableFact]
    public async Task CreateAndGetEvent_ReturnsFieldsAndExpenseCountWithoutEmbeddedExpenses()
    {
        using var client = await CreateAuthorizedClientAsync();

        var created = await CreateEventAsync(client, new
        {
            name = "Đà Lạt 3 ngày",
            description = "Chuyến đi công ty",
            startDate = Day14,
            endDate = Day16
        });

        using var response = await client.GetAsync($"api/v1/events/{Uuid(created)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var evt = envelope.RootElement.GetProperty("data");

        Assert.Equal("Đà Lạt 3 ngày", evt.GetProperty("name").GetString());
        Assert.Equal("Chuyến đi công ty", evt.GetProperty("description").GetString());
        Assert.False(evt.GetProperty("isClosed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, evt.GetProperty("closedAt").ValueKind);
        Assert.Equal(0, evt.GetProperty("expenseCount").GetInt32());
        Assert.False(evt.TryGetProperty("expenses", out _)); // OQ15: no embedded expense list
    }

    [SkippableFact]
    public async Task GetEvent_WithAssignedExpense_ReportsExpenseCount()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
            eventUuid = evt,
            shares = new[] { new { memberUuid = an, amount = 100_000m } }
        });

        using var response = await client.GetAsync($"api/v1/events/{evt}");
        using var envelope = await ReadEnvelopeAsync(response);

        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("expenseCount").GetInt32());
    }

    [SkippableFact]
    public async Task CreateAndGetEvent_WithNoExpenses_TotalAdvancedZeroAndUpdatedAtPresent()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateEventAsync(client, new { name = "Đà Lạt", startDate = Day14, endDate = Day16 });

        var evt = await GetEventAsync(client, Uuid(created));

        Assert.Equal(0m, evt.GetProperty("totalAdvanced").GetDecimal());
        // updatedAt is exposed (the event's own row timestamp when there is no child activity).
        Assert.Equal(JsonValueKind.String, evt.GetProperty("updatedAt").ValueKind);
        Assert.True(evt.GetProperty("updatedAt").GetDateTime() >= evt.GetProperty("createdAt").GetDateTime());
    }

    [SkippableFact]
    public async Task GetEvent_WithExpenses_ExposesTotalAdvancedAndEffectiveUpdatedAt()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(client);
        var an = await CreateMemberAsync(client, "An");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 100_000m },
                new { memberUuid = ownerRep, amount = 50_000m }
            }
        });

        var data = await GetEventAsync(client, evt);

        Assert.Equal(150_000m, data.GetProperty("totalAdvanced").GetDecimal());
        // Adding the expense/shares bubbles the effective updatedAt to at least the event's createdAt.
        Assert.True(data.GetProperty("updatedAt").GetDateTime() >= data.GetProperty("createdAt").GetDateTime());
    }

    [SkippableFact]
    public async Task ListEvents_ExposesTotalAdvancedPerEvent()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(client);
        var withExpense = await CreateEventUuidAsync(client, "Có phiếu", Day14, Day16);
        await CreateEventUuidAsync(client, "Rỗng", Day14.AddDays(-3), Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
            eventUuid = withExpense,
            shares = new[] { new { memberUuid = ownerRep, amount = 250_000m } }
        });

        var byName = (await ListEventsAsync(client))
            .ToDictionary(evt => evt.GetProperty("name").GetString()!, evt => evt.GetProperty("totalAdvanced").GetDecimal());

        Assert.Equal(250_000m, byName["Có phiếu"]);
        Assert.Equal(0m, byName["Rỗng"]);
    }

    [SkippableFact]
    public async Task ListEvents_SortsByStartDateDescending()
    {
        using var client = await CreateAuthorizedClientAsync();
        await CreateEventUuidAsync(client, "Older", Day14.AddDays(-5), Day14.AddDays(-4));
        await CreateEventUuidAsync(client, "Newer", Day14, Day16);
        await CreateEventUuidAsync(client, "Middle", Day14.AddDays(-2), Day14.AddDays(-1));

        var names = (await ListEventsAsync(client)).Select(evt => evt.GetProperty("name").GetString());

        Assert.Equal(["Newer", "Middle", "Older"], names);
    }

    [SkippableFact]
    public async Task ListEvents_ClosedFilter_ReturnsOnlyMatchingState()
    {
        using var client = await CreateAuthorizedClientAsync();
        await CreateEventUuidAsync(client, "Open", Day14, Day16);
        var toClose = await CreateEventUuidAsync(client, "Closed", Day14.AddDays(-1), Day16);
        await CloseEventAsync(client, toClose);

        var closed = (await ListEventsAsync(client, "?closed=true")).Select(evt => evt.GetProperty("name").GetString());
        var open = (await ListEventsAsync(client, "?closed=false")).Select(evt => evt.GetProperty("name").GetString());

        Assert.Equal(["Closed"], closed);
        Assert.Equal(["Open"], open);
    }

    [SkippableFact]
    public async Task UpdateEvent_ChangesInfo()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        using var response = await client.PutAsJsonAsync($"api/v1/events/{evt}", new
        {
            name = "Đà Lạt (sửa)",
            description = "Cập nhật",
            startDate = Day14,
            endDate = Day16.AddDays(2)
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);

        Assert.Equal("Đà Lạt (sửa)", envelope.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task DeleteEvent_Open_RemovesItAndSubsequentGetIs404()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        using var delete = await client.DeleteAsync($"api/v1/events/{evt}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var get = await client.GetAsync($"api/v1/events/{evt}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var envelope = await ReadEnvelopeAsync(get);
        AssertErrorEnvelope(envelope, ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task CloseEvent_IsOneWay_AndBlocksUpdateDeleteRecloseWhileStayingClosed()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        await CloseEventAsync(client, evt);

        // The event is now closed with a closedAt timestamp.
        var afterClose = await GetEventAsync(client, evt);
        Assert.True(afterClose.GetProperty("isClosed").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, afterClose.GetProperty("closedAt").ValueKind);

        // Re-close -> 400 9001.
        using var reclose = await client.PutAsync($"api/v1/events/{evt}/close", null);
        Assert.Equal(HttpStatusCode.BadRequest, reclose.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(reclose), ErrorCodes.EventClosed);

        // Update -> 400 9001.
        using var update = await client.PutAsJsonAsync($"api/v1/events/{evt}", new { name = "Sửa", startDate = Day14, endDate = Day16 });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(update), ErrorCodes.EventClosed);

        // Delete -> 400 9001.
        using var delete = await client.DeleteAsync($"api/v1/events/{evt}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(delete), ErrorCodes.EventClosed);

        // Still closed and still there.
        Assert.True((await GetEventAsync(client, evt)).GetProperty("isClosed").GetBoolean());
    }

    [SkippableFact]
    public async Task AnotherUsersEvent_Returns404Code9000OnEveryRoute_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(owner, "Đà Lạt", Day14, Day16);

        using var get = await stranger.GetAsync($"api/v1/events/{evt}");
        using var update = await stranger.PutAsJsonAsync($"api/v1/events/{evt}", new { name = "Hack", startDate = Day14, endDate = Day16 });
        using var close = await stranger.PutAsync($"api/v1/events/{evt}/close", null);
        using var delete = await stranger.DeleteAsync($"api/v1/events/{evt}");

        foreach (var response in new[] { get, update, close, delete })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, never 403
            AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
        }

        // The owner's event is untouched.
        Assert.Equal("Đà Lạt", (await GetEventAsync(owner, evt)).GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task Events_AnonymousRequest_Returns401()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var list = await client.GetAsync("api/v1/events");
        using var create = await client.PostAsJsonAsync("api/v1/events", new { name = "X", startDate = Day14, endDate = Day16 });

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(list), ErrorCodes.Unauthorized);
    }

    [SkippableFact]
    public async Task CreateEvent_EndDateBeforeStartDate_Returns400WithCamelCaseFields()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/events", new
        {
            name = "Ngược ngày",
            startDate = Day16,
            endDate = Day14 // end < start
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed); // 1001, not a 9xxx business code
        var fields = envelope.RootElement.GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("endDate", out var endDateErrors)); // camelCase key
        Assert.True(endDateErrors.GetArrayLength() >= 1);
    }

    [SkippableFact]
    public async Task CreateEvent_EmptyName_Returns400WithNameField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/events", new { name = "", startDate = Day14, endDate = Day16 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("name", out _));
    }

    private static async Task<JsonElement> GetEventAsync(HttpClient client, string uuid)
    {
        using var response = await client.GetAsync($"api/v1/events/{uuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
    }
}
