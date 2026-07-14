using System.Net;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for <c>GET api/v1/events/{uuid}/balance</c> via WebApplicationFactory (real
/// MariaDB/Redis - skippable). Drives the §3.7 scenario through the M5/M6 create endpoints, then reads
/// the balance and asserts the per-member figures + the sum-to-zero invariant over the wire; also covers
/// the owner-rep-at-0đ auto-share row, the owned-but-empty event (200 empty rows), the resource-owned
/// 404 (code 9000, never 403) for another user's event, and the anonymous 401. Assertions target the
/// full <c>ApiResult</c> envelope + stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class EventBalanceEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<JsonElement> GetBalanceDataAsync(HttpClient client, string eventUuid)
    {
        using var response = await client.GetAsync($"api/v1/events/{eventUuid}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());
        return envelope.RootElement.GetProperty("data").Clone();
    }

    private static JsonElement RowFor(JsonElement data, string memberUuid) =>
        data.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("memberUuid").GetString() == memberUuid);

    [SkippableFact]
    public async Task GetBalance_Scenario_ReturnsPerMemberFiguresAndSumsToZero()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var cuong = await CreateMemberAsync(client, "Cường");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        // Bình advanced 800k.
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 300_000m },
                new { memberUuid = binh, amount = 200_000m },
                new { memberUuid = cuong, amount = 300_000m }
            }
        });
        // An advanced 700k.
        await CreateExpenseAsync(client, new
        {
            name = "Khách sạn",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 200_000m },
                new { memberUuid = binh, amount = 300_000m },
                new { memberUuid = cuong, amount = 200_000m }
            }
        });

        var data = await GetBalanceDataAsync(client, evt);

        Assert.Equal(evt, data.GetProperty("eventUuid").GetString());
        Assert.Equal("Đà Lạt", data.GetProperty("eventName").GetString());
        Assert.False(data.GetProperty("isClosed").GetBoolean());

        var rows = data.GetProperty("rows");
        Assert.Equal(3, rows.GetArrayLength());
        Assert.Equal(200_000m, RowFor(data, an).GetProperty("balance").GetDecimal());
        Assert.Equal(300_000m, RowFor(data, binh).GetProperty("balance").GetDecimal());
        Assert.Equal(-500_000m, RowFor(data, cuong).GetProperty("balance").GetDecimal());

        var sum = rows.EnumerateArray().Sum(row => row.GetProperty("balance").GetDecimal());
        Assert.Equal(0m, sum); // sum-to-zero over HTTP
    }

    [SkippableFact]
    public async Task GetBalance_OwnerRepAutoZeroShare_AppearsInRows()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Cà phê", Day14, Day16);

        // Only Bình bears; the owner-rep share is auto-added at 0đ by the create endpoint.
        await CreateExpenseAsync(client, new
        {
            name = "Cà phê",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });

        var data = await GetBalanceDataAsync(client, evt);

        var anRow = RowFor(data, an);
        Assert.True(anRow.GetProperty("isOwnerRepresentative").GetBoolean());
        Assert.Equal(0m, anRow.GetProperty("advanced").GetDecimal());
        Assert.Equal(0m, anRow.GetProperty("owed").GetDecimal()); // owner-rep at 0đ still appears (OQ3)
    }

    [SkippableFact]
    public async Task GetBalance_OwnedEmptyEvent_Returns200WithEmptyRows()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Trống", Day14, Day16);

        var data = await GetBalanceDataAsync(client, evt);

        Assert.Empty(data.GetProperty("rows").EnumerateArray()); // OQ15
    }

    [SkippableFact]
    public async Task GetBalance_AnotherUsersEvent_Returns404Code9000_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(owner, "Đà Lạt", Day14, Day16);

        using var response = await stranger.GetAsync($"api/v1/events/{evt}/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task GetBalance_UnknownEvent_Returns404Code9000()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.GetAsync("api/v1/events/no-such-uuid/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task GetBalance_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var response = await client.GetAsync("api/v1/events/some-uuid/balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }
}
