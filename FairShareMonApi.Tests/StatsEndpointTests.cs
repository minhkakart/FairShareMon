using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the two stats endpoints (<c>GET api/v1/stats/overview</c> and
/// <c>GET api/v1/stats/by-category</c>) via WebApplicationFactory (real MariaDB/Redis - skippable).
/// Covers overview totals over loose+event expenses with a range and all-time, by-category in both
/// time-range and event modes (with color/icon + total-DESC sort), the from &gt; to and both-scopes
/// 400/1001 validations with camelCase <c>error.fields</c>, the foreign-eventUuid 404/9000, per-user
/// isolation, and the anonymous 401. Assertions target the full <c>ApiResult</c> envelope + stable CODES.
/// </summary>
[Collection("AuthIntegration")]
public class StatsEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("yyyy-MM-ddTHH:mm:ssZ"));

    private static async Task<string> CreateCategoryAsync(HttpClient client, string name, string color, string? icon = null)
    {
        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name, color, icon });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());
        return envelope.RootElement.GetProperty("data").Clone();
    }

    // ---- Overview ----------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Overview_SumsLooseAndEventExpenses_InRangeAndAllTime()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var evt = await CreateEventUuidAsync(client, "Đợt", Day14, Day16);

        await CreateExpenseAsync(client, new
        {
            name = "Trong đợt", expenseTime = Day15Noon, eventUuid = evt,
            shares = new[] { new { memberUuid = an, amount = 300_000m } }
        });
        await CreateExpenseAsync(client, new
        {
            name = "Phiếu rời", expenseTime = Day15Noon,
            shares = new[] { new { memberUuid = an, amount = 200_000m } }
        });
        await CreateExpenseAsync(client, new
        {
            name = "Ngoài khoảng", expenseTime = Day16.AddDays(30),
            shares = new[] { new { memberUuid = an, amount = 999_000m } }
        });

        var inRange = await GetDataAsync(client, $"api/v1/stats/overview?from={Iso(Day14.AddDays(-1))}&to={Iso(Day16.AddDays(1))}");
        Assert.Equal(500_000m, inRange.GetProperty("totalSpending").GetDecimal()); // loose + event, out-of-range excluded
        Assert.Equal(2, inRange.GetProperty("expenseCount").GetInt32());

        var allTime = await GetDataAsync(client, "api/v1/stats/overview");
        Assert.Equal(1_499_000m, allTime.GetProperty("totalSpending").GetDecimal());
        Assert.Equal(3, allTime.GetProperty("expenseCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, allTime.GetProperty("from").ValueKind); // bounds echoed as null
        Assert.Equal(JsonValueKind.Null, allTime.GetProperty("to").ValueKind);
    }

    [SkippableFact]
    public async Task Overview_FromAfterTo_Returns400Code1001WithCamelCaseField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.GetAsync($"api/v1/stats/overview?from={Iso(Day16)}&to={Iso(Day14)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed); // 1001, not a 9xxx business code
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("to", out _)); // camelCase key
    }

    [SkippableFact]
    public async Task Overview_IsPerUserIsolated()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(owner);
        var strangerRep = await OwnerRepUuidAsync(stranger);
        await CreateExpenseAsync(owner, new { name = "Của tôi", expenseTime = Day15Noon, shares = new[] { new { memberUuid = ownerRep, amount = 100_000m } } });
        await CreateExpenseAsync(stranger, new { name = "Của người khác", expenseTime = Day15Noon, shares = new[] { new { memberUuid = strangerRep, amount = 777_000m } } });

        var overview = await GetDataAsync(owner, "api/v1/stats/overview");

        Assert.Equal(100_000m, overview.GetProperty("totalSpending").GetDecimal()); // stranger's spend never counted
        Assert.Equal(1, overview.GetProperty("expenseCount").GetInt32());
    }

    [SkippableFact]
    public async Task Overview_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/stats/overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ---- By-category -------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ByCategory_TimeRange_GroupsPerCategorySortedTotalDesc()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var defaultCategory = await DefaultCategoryAsync(client);
        var defaultUuid = defaultCategory.GetProperty("uuid").GetString()!;
        var travel = await CreateCategoryAsync(client, "Di chuyển", "#3B82F6", "car");

        // Default category: 100k + 200k = 300k over 2 expenses; Di chuyển: 500k over 1 expense.
        await CreateExpenseAsync(client, new { name = "A", expenseTime = Day15Noon, shares = new[] { new { memberUuid = an, amount = 100_000m } } });
        await CreateExpenseAsync(client, new { name = "B", expenseTime = Day15Noon, shares = new[] { new { memberUuid = an, amount = 200_000m } } });
        await CreateExpenseAsync(client, new { name = "C", expenseTime = Day15Noon, categoryUuid = travel, shares = new[] { new { memberUuid = an, amount = 500_000m } } });

        var data = await GetDataAsync(client, $"api/v1/stats/by-category?from={Iso(Day14.AddDays(-1))}&to={Iso(Day16.AddDays(1))}");

        var rows = data.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        // Sorted total DESC: Di chuyển 500k first, then default 300k.
        Assert.Equal(travel, rows[0].GetProperty("categoryUuid").GetString());
        Assert.Equal(500_000m, rows[0].GetProperty("total").GetDecimal());
        Assert.Equal("car", rows[0].GetProperty("icon").GetString());

        Assert.Equal(defaultUuid, rows[1].GetProperty("categoryUuid").GetString());
        Assert.Equal(300_000m, rows[1].GetProperty("total").GetDecimal());
        Assert.Equal(2, rows[1].GetProperty("expenseCount").GetInt32());
    }

    [SkippableFact]
    public async Task ByCategory_EventMode_ScopesToTheEvent()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var evt = await CreateEventUuidAsync(client, "Đợt", Day14, Day16);
        await CreateExpenseAsync(client, new { name = "Trong đợt", expenseTime = Day15Noon, eventUuid = evt, shares = new[] { new { memberUuid = an, amount = 400_000m } } });
        await CreateExpenseAsync(client, new { name = "Phiếu rời", expenseTime = Day15Noon, shares = new[] { new { memberUuid = an, amount = 999_000m } } });

        var data = await GetDataAsync(client, $"api/v1/stats/by-category?eventUuid={evt}");

        Assert.Equal(evt, data.GetProperty("eventUuid").GetString());
        var row = Assert.Single(data.GetProperty("rows").EnumerateArray().ToArray());
        Assert.Equal(400_000m, row.GetProperty("total").GetDecimal()); // loose expense not counted
    }

    [SkippableFact]
    public async Task ByCategory_BothScopes_Returns400Code1001()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Đợt", Day14, Day16);

        using var response = await client.GetAsync($"api/v1/stats/by-category?eventUuid={evt}&from={Iso(Day14)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed); // 1001 - never silently drops a filter (OQ8)
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("eventUuid", out _));
    }

    [SkippableFact]
    public async Task ByCategory_ForeignEventUuid_Returns404Code9000_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(owner, "Đợt", Day14, Day16);

        using var response = await stranger.GetAsync($"api/v1/stats/by-category?eventUuid={evt}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task ByCategory_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/stats/by-category");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }
}
