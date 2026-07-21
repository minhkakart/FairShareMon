using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the settled-per-member feature (§6) via WebApplicationFactory (real
/// MariaDB/Redis - skippable): the per-share toggle <c>PUT /expenses/{uuid}/shares/{shareUuid}/settled</c>
/// (Layer A + whole-expense reconcile), the per-member net-clearance toggle
/// <c>PUT /events/{uuid}/members/{memberUuid}/settled</c> (Layer B), the additive overlay on
/// <c>GET /events/{uuid}/balance</c>, and the event-QR "bill only uncleared owing members" behaviour
/// (OQ13a). Asserts the full <c>ApiResult</c> envelope, real HTTP status codes, the D2 balance-purity
/// invariant over the wire, and stable error CODES (7000/6000/9000/3000, never 403).
/// </summary>
[Collection("AuthIntegration")]
public class SettledPerMemberEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static JsonElement ShareForMember(JsonElement expense, string memberUuid) =>
        expense.GetProperty("shares").EnumerateArray()
            .Single(share => share.GetProperty("member").GetProperty("uuid").GetString() == memberUuid);

    private static async Task<JsonElement> GetBalanceDataAsync(HttpClient client, string eventUuid)
    {
        using var response = await client.GetAsync($"api/v1/events/{eventUuid}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
    }

    private static JsonElement RowFor(JsonElement data, string memberUuid) =>
        data.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("memberUuid").GetString() == memberUuid);

    private static async Task CreateBankAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ============================ Layer A: per-share settled toggle ============================

    [SkippableFact]
    public async Task SetShareSettled_MarksShareSettledAndReconcilesWholeExpense()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        // Owner-rep pays; Bình's 100k is the only billable share (owner-rep auto-0đ is non-billable).
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            shares = new[] { new { memberUuid = binh, amount = 100_000m } }
        });
        var binhShareUuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(created)), binh));

        using var response = await client.PutAsJsonAsync(
            $"api/v1/expenses/{Uuid(created)}/shares/{binhShareUuid}/settled", new { isSettled = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expense = await GetExpenseAsync(client, Uuid(created));
        var binhShare = ShareForMember(expense, binh);
        Assert.True(binhShare.GetProperty("isSettled").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, binhShare.GetProperty("settledAt").ValueKind);
        Assert.Equal(100_000m, binhShare.GetProperty("amount").GetDecimal()); // amount untouched (§4.3)
        Assert.True(expense.GetProperty("isSettled").GetBoolean()); // all billable shares settled ⇒ expense settled (OQ3a)
    }

    [SkippableFact]
    public async Task SetShareSettled_OnClosedEventExpense_Succeeds()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 100_000m } }
        });
        var binhShareUuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(created)), binh));
        await CloseEventAsync(client, evt);

        using var response = await client.PutAsJsonAsync(
            $"api/v1/expenses/{Uuid(created)}/shares/{binhShareUuid}/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // §4.4 sole exception (OQ5a)
    }

    [SkippableFact]
    public async Task SetShareSettled_AnotherUsersExpense_Returns404Code6000_Never403()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(owner);
        var binh = await CreateMemberAsync(owner, "Bình");
        var created = await CreateExpenseAsync(owner, new
        {
            name = "Ăn trưa",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            shares = new[] { new { memberUuid = binh, amount = 100_000m } }
        });
        var binhShareUuid = Uuid(ShareForMember(await GetExpenseAsync(owner, Uuid(created)), binh));

        using var response = await stranger.PutAsJsonAsync(
            $"api/v1/expenses/{Uuid(created)}/shares/{binhShareUuid}/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // expense scoped to caller → 6000
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ExpenseNotFound);
    }

    [SkippableFact]
    public async Task SetShareSettled_UnknownShareOnOwnedExpense_Returns404Code7000()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Day15Noon });

        using var response = await client.PutAsJsonAsync(
            $"api/v1/expenses/{Uuid(created)}/shares/no-such-share/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareNotFound);
    }

    [SkippableFact]
    public async Task SetShareSettled_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "api/v1/expenses/some-uuid/shares/some-share/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ============================ Layer B: per-member net-clearance toggle ============================

    [SkippableFact]
    public async Task SetMemberSettled_Participant_Succeeds()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });

        using var response = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());
    }

    [SkippableFact]
    public async Task SetMemberSettled_NonParticipant_Returns404Code3000()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var outsider = await CreateMemberAsync(client, "Không tham gia"); // owned but not in the event
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });

        using var response = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{outsider}/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.MemberNotFound);
    }

    [SkippableFact]
    public async Task SetMemberSettled_AnotherUsersEvent_Returns404Code9000()
    {
        using var owner = await CreateAuthorizedClientAsync();
        using var stranger = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(owner);
        var binh = await CreateMemberAsync(owner, "Bình");
        var evt = await CreateEventUuidAsync(owner, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(owner, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });
        var strangerMember = await CreateMemberAsync(stranger, "Bình");

        using var response = await stranger.PutAsJsonAsync($"api/v1/events/{evt}/members/{strangerMember}/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound); // event resolved first, scoped to caller
    }

    [SkippableFact]
    public async Task SetMemberSettled_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PutAsJsonAsync("api/v1/events/some-event/members/some-member/settled", new { isSettled = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ============================ Balance overlay (D2 preserved) ============================

    [SkippableFact]
    public async Task GetBalance_CarriesOverlayFields_AndMarkingSettledZeroesOutstandingWithoutTouchingBalance()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        // An advances 500k, Bình owes it all → An +500k, Bình −500k.
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });

        var before = await GetBalanceDataAsync(client, evt);
        // Overlay fields present; Bình still owes, nobody cleared yet.
        Assert.Equal(500_000m, RowFor(before, binh).GetProperty("outstanding").GetDecimal());
        Assert.False(RowFor(before, binh).GetProperty("isSettled").GetBoolean());
        Assert.Equal(0m, RowFor(before, an).GetProperty("outstanding").GetDecimal()); // An is owed, not owing
        Assert.Equal(500_000m, before.GetProperty("totalOutstanding").GetDecimal());
        Assert.Equal(1, before.GetProperty("owingMemberCount").GetInt32());
        Assert.Equal(0, before.GetProperty("settledMemberCount").GetInt32());

        // Mark Bình cleared (Layer B), then re-read the overlay.
        using (var mark = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        var after = await GetBalanceDataAsync(client, evt);

        var binhAfter = RowFor(after, binh);
        Assert.True(binhAfter.GetProperty("isSettled").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, binhAfter.GetProperty("settledAt").ValueKind);
        Assert.Equal(0m, binhAfter.GetProperty("outstanding").GetDecimal());  // outstanding zeroed
        Assert.Equal(0m, after.GetProperty("totalOutstanding").GetDecimal());
        Assert.Equal(0, after.GetProperty("owingMemberCount").GetInt32());
        Assert.Equal(1, after.GetProperty("settledMemberCount").GetInt32());

        // D2 / M7 OQ2: advanced/owed/balance are byte-for-byte identical before and after the settled mark.
        foreach (var uuid in new[] { an, binh })
        {
            Assert.Equal(RowFor(before, uuid).GetProperty("advanced").GetDecimal(), RowFor(after, uuid).GetProperty("advanced").GetDecimal());
            Assert.Equal(RowFor(before, uuid).GetProperty("owed").GetDecimal(), RowFor(after, uuid).GetProperty("owed").GetDecimal());
            Assert.Equal(RowFor(before, uuid).GetProperty("balance").GetDecimal(), RowFor(after, uuid).GetProperty("balance").GetDecimal());
        }
        Assert.Equal(0m, after.GetProperty("rows").EnumerateArray().Sum(row => row.GetProperty("balance").GetDecimal())); // sum-to-zero
    }

    // ============================ Event QR bills only uncleared owing members (OQ13a) ============================

    [SkippableFact]
    public async Task EventQr_MarkOneOfTwoDebtorsSettled_StillBilled_ThenBothSettled_Returns12003()
    {
        using var client = await CreatePremiumClientAsync(); // QR is Premium-gated (M10)
        await CreateBankAccountAsync(client);
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var cuong = await CreateMemberAsync(client, "Cường");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        // An advances 800k; Bình owes 300k, Cường owes 500k → two owing members.
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = binh, amount = 300_000m },
                new { memberUuid = cuong, amount = 500_000m }
            }
        });
        await CloseEventAsync(client, evt);

        // Mark Bình cleared; Cường still owes → the QR still renders (a PNG).
        using (var mark = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        using (var qr = await client.GetAsync($"api/v1/events/{evt}/qr"))
        {
            Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
            Assert.Equal("image/png", qr.Content.Headers.ContentType!.ToString());
        }

        // Mark Cường cleared too → nobody owes → 12003 (OQ13a widened semantics).
        using (var mark = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{cuong}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        using (var qr = await client.GetAsync($"api/v1/events/{evt}/qr"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, qr.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(qr), ErrorCodes.NoOutstandingDebtForQr);
        }
    }

    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();
            await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
