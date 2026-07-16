using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for timezone-aware DateTimes via WebApplicationFactory (real MariaDB/Redis -
/// skippable), driving the full production pipeline (RequestTimeZoneMiddleware + STJ converters + pooled
/// DbContext with the UTC session interceptor). Proves:
/// <list type="bullet">
/// <item>The SAME stored instant renders with a different offset per <c>X-Time-Zone</c> header (IANA id,
/// <c>+07:00</c>, <c>+7</c>, <c>+00:00</c>, a western IANA zone, garbage, and no header -&gt; app-default),
/// always the same absolute moment - composed inside the <c>ApiResult&lt;T&gt;</c> envelope.</item>
/// <item>A naive inbound <c>expense_time</c> under <c>+07:00</c> is interpreted in that zone and stored as
/// the earlier UTC instant; GET round-trips it back with <c>+07:00</c> and reveals the UTC via <c>+00:00</c>.</item>
/// <item>An event created under a +7 header covers the whole local day; the CSV export (a
/// <c>FileContentResult</c> bypassing the JSON converters) renders calendar dates in the request zone and
/// honors a different <c>X-Time-Zone</c>.</item>
/// </list>
/// </summary>
[Collection("AuthIntegration")]
public class TimeZoneEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private const string TimeZoneHeader = "X-Time-Zone";

    // ---- helpers ----------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body, string? timeZone)
    {
        using var request = new HttpRequestMessage(method, url);
        if (timeZone is not null)
            request.Headers.Add(TimeZoneHeader, timeZone);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url, string? timeZone)
    {
        using var response = await SendAsync(client, HttpMethod.Get, url, null, timeZone);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean()); // composes with ApiResult<T>
        return envelope.RootElement.GetProperty("data").Clone();
    }

    private static DateTimeOffset ParseOffset(JsonElement data, string property) =>
        DateTimeOffset.Parse(data.GetProperty(property).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static async Task<string> ExportCsvAsync(HttpClient client, string url, string? timeZone)
    {
        using var response = await SendAsync(client, HttpMethod.Get, url, null, timeZone);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
    }

    // ---- Offset rendering per header (same instant) ------------------------------------------------

    [SkippableFact]
    public async Task GetExpense_RendersStoredInstant_WithRequestZoneOffset_PerHeader()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(client);

        // Created with an explicit-UTC (Z) instant, so storage is exactly 12:00Z regardless of header.
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Noon, // 2026-07-14 12:00 Utc -> serialized "...Z"
            shares = new[] { new { memberUuid = ownerRep, amount = 0m } }
        });
        var uuid = Uuid(created);
        var storedInstant = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        var cases = new (string? Header, TimeSpan Offset)[]
        {
            ("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7)),
            ("+07:00", TimeSpan.FromHours(7)),
            ("+7", TimeSpan.FromHours(7)),
            ("+00:00", TimeSpan.Zero),
            (null, TimeSpan.FromHours(7)),        // no header -> app-default (+7)
            ("Not/AZone", TimeSpan.FromHours(7)), // garbage -> silent fallback to default (+7)
        };

        foreach (var (header, offset) in cases)
        {
            var data = await GetDataAsync(client, $"api/v1/expenses/{uuid}", header);
            var rendered = ParseOffset(data, "expenseTime");

            Assert.Equal(offset, rendered.Offset);                 // rendered in the viewer's zone
            Assert.Equal(storedInstant, rendered.ToUniversalTime()); // same absolute instant
        }

        // A western IANA zone: same instant, a negative offset that is clearly not the +7 default.
        var ny = await GetDataAsync(client, $"api/v1/expenses/{uuid}", "America/New_York");
        var nyTime = ParseOffset(ny, "expenseTime");
        Assert.Equal(storedInstant, nyTime.ToUniversalTime());
        Assert.True(nyTime.Offset < TimeSpan.Zero);
        Assert.NotEqual(TimeSpan.FromHours(7), nyTime.Offset);
    }

    // ---- Naive inbound -> request zone -> UTC (round-trip) ----------------------------------------

    [SkippableFact]
    public async Task PostExpense_NaiveTimeUnderPlus7_StoredAsEarlierUtc_AndRoundTrips()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(client);

        // Naive (no offset) local midnight under +07:00 -> 2026-07-13T17:00:00Z stored.
        using var post = await SendAsync(client, HttpMethod.Post, "api/v1/expenses", new
        {
            name = "Nửa đêm",
            expenseTime = "2026-07-14T00:00:00", // naive string, no offset
            shares = new[] { new { memberUuid = ownerRep, amount = 0m } }
        }, "+07:00");
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        using var postEnvelope = await ReadEnvelopeAsync(post);
        var uuid = postEnvelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;

        // GET under +07:00 shows the local midnight the user typed, with +07:00.
        var plus7 = await GetDataAsync(client, $"api/v1/expenses/{uuid}", "+07:00");
        var plus7Time = ParseOffset(plus7, "expenseTime");
        Assert.Equal(TimeSpan.FromHours(7), plus7Time.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.FromHours(7)), plus7Time);

        // GET under +00:00 reveals the stored UTC is the earlier instant (naive -> request-zone -> UTC).
        var utc = await GetDataAsync(client, $"api/v1/expenses/{uuid}", "+00:00");
        var utcTime = ParseOffset(utc, "expenseTime");
        Assert.Equal(new DateTimeOffset(2026, 7, 13, 17, 0, 0, TimeSpan.Zero), utcTime);
    }

    // ---- Event range + CSV export honor the request zone -----------------------------------------

    [SkippableFact]
    public async Task PostEvent_NaiveDatesUnderPlus7_RangeCoversLocalDay_AndCsvHonorsZone()
    {
        using var client = await CreateAuthorizedClientAsync();

        // Naive calendar dates under +07:00: the whole day 14/07 in +7.
        using var post = await SendAsync(client, HttpMethod.Post, "api/v1/events", new
        {
            name = "Đà Lạt",
            startDate = "2026-07-14T00:00:00",
            endDate = "2026-07-14T00:00:00"
        }, "+07:00");
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        using var postEnvelope = await ReadEnvelopeAsync(post);
        var evtUuid = postEnvelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;

        // GET the event under +07:00: the range covers the whole local day 14/07.
        var evt = await GetDataAsync(client, $"api/v1/events/{evtUuid}", "+07:00");
        var start = ParseOffset(evt, "startDate");
        var end = ParseOffset(evt, "endDate");
        Assert.Equal(TimeSpan.FromHours(7), start.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.FromHours(7)), start);
        Assert.Equal(TimeSpan.FromHours(7), end.Offset);
        // Local end = 14/07 23:59:59.999999 (+7) == 14/07 16:59:59.999999Z.
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 17, 0, 0, TimeSpan.Zero).AddTicks(-10), end.ToUniversalTime());

        // CSV export (FileContentResult, bypasses the JSON converters) under +07:00 -> 14/07 - 14/07.
        var csvPlus7 = await ExportCsvAsync(client, $"api/v1/events/{evtUuid}/export", "+07:00");
        Assert.Contains("14/07/2026 - 14/07/2026", csvPlus7);

        // The SAME event under a western zone: the calendar date tracks that zone (13/07 - 14/07).
        var csvNy = await ExportCsvAsync(client, $"api/v1/events/{evtUuid}/export", "America/New_York");
        Assert.Contains("13/07/2026 - 14/07/2026", csvNy);
    }
}
