using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests (real MariaDB/Redis, skippable) for the two expense-side event routes
/// (<c>PUT /expenses/{uuid}/event</c> assign/move, <c>DELETE /expenses/{uuid}/event</c> remove),
/// create-into-event (OQ5), the <c>?eventUuid</c>/<c>?looseOnly</c> list filters and the inline event
/// fields on the expense DTOs (OQ14), and the milestone's §4.4 core: once the event is CLOSED, EVERY
/// M5 expense/share write route returns 400 (9001) while <c>PUT /expenses/{uuid}/settled</c> still
/// succeeds. Also covers assign out-of-range (9002), another user's event on assign (404 9000, never
/// 403), and the anonymous 401. Assertions target stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class ExpenseEventEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mid15 = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime JustAfterEnd = new(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<JsonElement[]> ListExpensesAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"api/v1/expenses{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    // ---- Assign / move / remove + filters + inline fields -----------------------------------------

    [SkippableFact]
    public async Task AssignExpenseToEvent_SetsInlineEventFields_ThenRemoveGoesLoose()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var expense = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Mid15,
            shares = new[] { new { memberUuid = an, amount = 100_000m } }
        });
        var expenseUuid = Uuid(expense);

        // Assign.
        using var assign = await client.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}/event", new { eventUuid = evt });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        using (var envelope = await ReadEnvelopeAsync(assign))
        {
            var data = envelope.RootElement.GetProperty("data");
            Assert.Equal(evt, data.GetProperty("eventUuid").GetString());
            Assert.Equal("Đà Lạt", data.GetProperty("eventName").GetString());
            Assert.False(data.GetProperty("eventIsClosed").GetBoolean());
        }

        // Visible inline on GET.
        var got = await GetExpenseAsync(client, expenseUuid);
        Assert.Equal(evt, got.GetProperty("eventUuid").GetString());
        Assert.Equal("Đà Lạt", got.GetProperty("eventName").GetString());

        // Filter ?eventUuid= includes it; ?looseOnly=true excludes it.
        Assert.Contains(await ListExpensesAsync(client, $"?eventUuid={evt}"), e => e.GetProperty("uuid").GetString() == expenseUuid);
        Assert.DoesNotContain(await ListExpensesAsync(client, "?looseOnly=true"), e => e.GetProperty("uuid").GetString() == expenseUuid);

        // Remove -> loose.
        using var remove = await client.DeleteAsync($"api/v1/expenses/{expenseUuid}/event");
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        var loose = await GetExpenseAsync(client, expenseUuid);
        Assert.Equal(JsonValueKind.Null, loose.GetProperty("eventUuid").ValueKind);
        Assert.Contains(await ListExpensesAsync(client, "?looseOnly=true"), e => e.GetProperty("uuid").GetString() == expenseUuid);
    }

    [SkippableFact]
    public async Task CreateExpenseIntoEvent_SetsEventInlineFields()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Mid15,
            eventUuid = evt,
            shares = new[] { new { memberUuid = an, amount = 100_000m } }
        });

        Assert.Equal(evt, created.GetProperty("eventUuid").GetString());
        Assert.Equal("Đà Lạt", created.GetProperty("eventName").GetString());
    }

    [SkippableFact]
    public async Task AssignExpenseToEvent_ExpenseTimeOutOfRange_Returns400Code9002()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var expense = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = JustAfterEnd, // outside [14,16]
            shares = new[] { new { memberUuid = an, amount = 100_000m } }
        });

        using var response = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(expense)}/event", new { eventUuid = evt });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ExpenseTimeOutOfEventRange);
    }

    [SkippableFact]
    public async Task AssignExpenseToAnotherUsersEvent_Returns404Code9000_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var strangerEvent = await CreateEventUuidAsync(stranger, "Của người khác", Day14, Day16);
        var an = await CreateMemberAsync(owner, "An");
        var expense = await CreateExpenseAsync(owner, new
        {
            name = "Ăn tối",
            expenseTime = Mid15,
            shares = new[] { new { memberUuid = an, amount = 100_000m } }
        });

        using var response = await owner.PutAsJsonAsync($"api/v1/expenses/{Uuid(expense)}/event", new { eventUuid = strangerEvent });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task AssignExpenseToEvent_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient(); // no token

        using var assign = await client.PutAsJsonAsync("api/v1/expenses/any/event", new { eventUuid = "any" });
        using var remove = await client.DeleteAsync("api/v1/expenses/any/event");

        Assert.Equal(HttpStatusCode.Unauthorized, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, remove.StatusCode);
    }

    // ---- Closed-event block through HTTP: every write route -> 9001; settled succeeds -------------

    [SkippableFact]
    public async Task ClosedEvent_BlocksEveryExpenseAndShareWriteRoute_ButSettledSucceeds()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var otherEvent = await CreateEventUuidAsync(client, "Đợt khác", Day14, Day16);

        var expense = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Mid15,
            eventUuid = evt,
            shares = new[] { new { memberUuid = an, amount = 100_000m } }
        });
        var expenseUuid = Uuid(expense);
        // The "An" share (non owner-rep) - used to exercise the share update/delete routes.
        var anShareUuid = expense.GetProperty("shares").EnumerateArray()
            .Single(share => share.GetProperty("member").GetProperty("uuid").GetString() == an)
            .GetProperty("uuid").GetString()!;

        await CloseEventAsync(client, evt);

        // Every M5 write route on the closed event's expense/shares -> 400 code 9001.
        using var update = await client.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}", new { name = "Sửa", expenseTime = Mid15 });
        using var addShare = await client.PostAsJsonAsync($"api/v1/expenses/{expenseUuid}/shares", new { memberUuid = binh, amount = 5_000m });
        using var updateShare = await client.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}/shares/{anShareUuid}", new { memberUuid = an, amount = 7_000m });
        using var moveEvent = await client.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}/event", new { eventUuid = otherEvent });
        using var removeEvent = await client.DeleteAsync($"api/v1/expenses/{expenseUuid}/event");
        using var deleteShare = await client.DeleteAsync($"api/v1/expenses/{expenseUuid}/shares/{anShareUuid}");
        using var deleteExpense = await client.DeleteAsync($"api/v1/expenses/{expenseUuid}");

        foreach (var response in new[] { update, addShare, updateShare, moveEvent, removeEvent, deleteShare, deleteExpense })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventClosed);
        }

        // The sole exception (§4.4): the settled toggle still succeeds.
        using var settled = await client.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}/settled", new { isSettled = true });
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        Assert.True((await GetExpenseAsync(client, expenseUuid)).GetProperty("isSettled").GetBoolean());
    }
}
