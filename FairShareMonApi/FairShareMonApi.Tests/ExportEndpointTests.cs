using System.Net;
using System.Text;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for <c>GET api/v1/expenses/{uuid}/export</c> and
/// <c>GET api/v1/events/{uuid}/export</c> via WebApplicationFactory (real MariaDB/Redis - skippable),
/// focused on the transport contract (M8, OQ1/OQ8/OQ9/OQ10/OQ16). Proves the success path returns
/// HTTP 200 with <c>Content-Type: text/csv; charset=utf-8</c>, a <c>Content-Disposition: attachment</c>
/// with the expected filename, and a body that is RAW CSV starting with the UTF-8 BOM (NOT an
/// <c>ApiResult</c> envelope) - the <c>[ResponseWrapped]</c> bypass; and that the error/edge paths still
/// return the wrapped envelope: an unsupported format → 400 (1001), another user's / unknown resource →
/// 404 (6000/9000, never 403), anonymous → 401; a closed event still exports (200). Default and
/// case-variant format both yield CSV.
/// </summary>
[Collection("AuthIntegration")]
public class ExportEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] Bom = Encoding.UTF8.GetPreamble();

    private async Task<string> CreateSimpleExpenseUuidAsync(HttpClient client)
    {
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var data = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            shares = new[]
            {
                new { memberUuid = an, amount = 0m },
                new { memberUuid = binh, amount = 500_000m }
            }
        });
        return Uuid(data);
    }

    private static bool StartsWithBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];

    private static bool ParsesAsApiResultEnvelope(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("isSuccess", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---- Success path (the bypass) -----------------------------------------------------------------

    [SkippableFact]
    public async Task ExportExpense_Default_Returns200CsvFileNotWrapped()
    {
        using var client = await CreateAuthorizedClientAsync();
        var expense = await CreateSimpleExpenseUuidAsync(client);

        using var response = await client.GetAsync($"api/v1/expenses/{expense}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Contains($"expense-{expense}-", disposition.FileName ?? disposition.FileNameStar ?? string.Empty);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(StartsWithBom(bytes)); // UTF-8 BOM (OQ3)
        Assert.False(ParsesAsApiResultEnvelope(bytes)); // raw CSV, not the ApiResult wrapper (OQ1)

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Tên phiếu", text); // Vietnamese header rendered
        Assert.DoesNotContain("isSuccess", text);
    }

    [SkippableFact]
    public async Task ExportExpense_FormatCsvUpperCase_Returns200Csv()
    {
        using var client = await CreateAuthorizedClientAsync();
        var expense = await CreateSimpleExpenseUuidAsync(client);

        using var response = await client.GetAsync($"api/v1/expenses/{expense}/export?format=CSV");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
    }

    [SkippableFact]
    public async Task ExportEvent_Default_Returns200CsvFileNotWrapped()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        using var response = await client.GetAsync($"api/v1/events/{evt}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Contains("event-", disposition.FileName ?? disposition.FileNameStar ?? string.Empty);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(StartsWithBom(bytes));
        Assert.False(ParsesAsApiResultEnvelope(bytes));
        Assert.Contains("Cân bằng nợ", Encoding.UTF8.GetString(bytes));
    }

    [SkippableFact]
    public async Task ExportEvent_ClosedEvent_Returns200()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 200_000m },
                new { memberUuid = binh, amount = 300_000m }
            }
        });
        await CloseEventAsync(client, evt);

        using var response = await client.GetAsync($"api/v1/events/{evt}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // export is read-only, works when closed (OQ16)
    }

    // ---- Error / edge paths (still wrapped) --------------------------------------------------------

    [SkippableFact]
    public async Task ExportExpense_UnsupportedFormat_Returns400Wrapped()
    {
        using var client = await CreateAuthorizedClientAsync();
        var expense = await CreateSimpleExpenseUuidAsync(client);

        using var response = await client.GetAsync($"api/v1/expenses/{expense}/export?format=xlsx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ValidationFailed);
    }

    [SkippableFact]
    public async Task ExportEvent_UnsupportedFormat_Returns400Wrapped()
    {
        using var client = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        using var response = await client.GetAsync($"api/v1/events/{evt}/export?format=xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ValidationFailed);
    }

    [SkippableFact]
    public async Task ExportExpense_AnotherUsersExpense_Returns404Code6000_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var expense = await CreateSimpleExpenseUuidAsync(owner);

        using var response = await stranger.GetAsync($"api/v1/expenses/{expense}/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ExpenseNotFound);
    }

    [SkippableFact]
    public async Task ExportEvent_AnotherUsersEvent_Returns404Code9000_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var evt = await CreateEventUuidAsync(owner, "Đà Lạt", Day14, Day16);

        using var response = await stranger.GetAsync($"api/v1/events/{evt}/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task ExportExpense_UnknownUuid_Returns404Code6000()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.GetAsync("api/v1/expenses/no-such-uuid/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ExpenseNotFound);
    }

    [SkippableFact]
    public async Task ExportEvent_UnknownUuid_Returns404Code9000()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.GetAsync("api/v1/events/no-such-uuid/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task ExportExpense_Anonymous_Returns401Wrapped()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var response = await client.GetAsync("api/v1/expenses/some-uuid/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    [SkippableFact]
    public async Task ExportEvent_Anonymous_Returns401Wrapped()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/events/some-uuid/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }
}
