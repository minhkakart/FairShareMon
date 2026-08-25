using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for event-expense-settlement-sync Milestone 1 (Direction 1 auto-cascade) via
/// WebApplicationFactory (real MariaDB/Redis - skippable). Drives
/// <c>PUT api/v1/events/{eventUuid}/members/{memberUuid}/settled</c> and reads back the resulting share
/// state via <c>GET api/v1/expenses/{uuid}</c> and the balance overlay via
/// <c>GET api/v1/events/{uuid}/balance</c>, per the planning doc's Step M1.5 endpoint test list.
/// </summary>
[Collection("AuthIntegration")]
public class EventSettlementCascadeEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
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

    [SkippableFact]
    public async Task SetMemberSettled_EligibleDebtor_CascadesToShareOnGet()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });

        using var response = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expense = await GetExpenseAsync(client, Uuid(created));
        var binhShare = ShareForMember(expense, binh);
        Assert.True(binhShare.GetProperty("isSettled").GetBoolean());
        Assert.True(expense.GetProperty("isSettled").GetBoolean()); // Bình is the only billable share
    }

    [SkippableFact]
    public async Task SetMemberSettled_GrossMixedCreditor_SharesUnaffected_OnlyBalanceOverlayFlips()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var cuong = await CreateMemberAsync(client, "Cường");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        // Expense X: An pays; Bình owes 300k -> An advances 300k.
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 300_000m } }
        });
        // Expense Y: Cường pays; An holds a genuine debtor-share of 200k -> An becomes gross-mixed.
        var expenseY = await CreateExpenseAsync(client, new
        {
            name = "Cà phê",
            expenseTime = Day15Noon,
            payerMemberUuid = cuong,
            eventUuid = evt,
            shares = new[] { new { memberUuid = an, amount = 200_000m } }
        });

        var balance = await GetBalanceDataAsync(client, evt);
        Assert.False(RowFor(balance, an).GetProperty("isEligibleForAutoCascade").GetBoolean()); // OQ-L / OQ4

        using var response = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{an}/settled", new { isSettled = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloadedY = await GetExpenseAsync(client, Uuid(expenseY));
        var anShareY = ShareForMember(reloadedY, an);
        Assert.False(anShareY.GetProperty("isSettled").GetBoolean()); // untouched - no cascade for a gross-mixed creditor

        var afterBalance = await GetBalanceDataAsync(client, evt);
        Assert.True(RowFor(afterBalance, an).GetProperty("isSettled").GetBoolean()); // Layer B flag still flips (OQ-A)
    }

    [SkippableFact]
    public async Task SetMemberSettled_UnsettleRoundTrip_UnconditionallyReversesLive()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });

        using (var settle = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settle.StatusCode);
        var settledExpense = await GetExpenseAsync(client, Uuid(created));
        Assert.True(ShareForMember(settledExpense, binh).GetProperty("isSettled").GetBoolean());

        using (var unsettle = await client.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = false }))
            Assert.Equal(HttpStatusCode.OK, unsettle.StatusCode);

        var reverted = await GetExpenseAsync(client, Uuid(created));
        var binhShare = ShareForMember(reverted, binh);
        Assert.False(binhShare.GetProperty("isSettled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, binhShare.GetProperty("settledAt").ValueKind);

        var balance = await GetBalanceDataAsync(client, evt);
        Assert.False(RowFor(balance, binh).GetProperty("isSettled").GetBoolean());
    }
}
