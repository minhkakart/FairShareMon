using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for event-expense-settlement-sync Milestone 2 (Direction 2 partial credit +
/// Story C QR remaining-amount math) via WebApplicationFactory (real MariaDB/Redis - skippable). Per the
/// planning doc's Step M2.6 endpoint test list: <c>GET /events/{uuid}/balance</c> reflects
/// <c>clearedAmount</c>/<c>outstanding</c>/<c>settlementStatus</c> after a partial per-share settle; the
/// closed-event QR (via the JSON <c>GET /events/{uuid}/qr/members</c> list, which shares the same
/// <c>Outstanding</c>-driven billing as the binary composite QR) bills exactly the remaining amount after
/// a partial credit, drops the member on full clearance, and the all-cleared case still returns
/// <c>NoOutstandingDebtForQr</c> (12003); and the whole-expense vs. per-share settled toggles produce an
/// identical resulting event balance for an equivalent single-share scenario (cross-trigger consistency,
/// end-to-end).
/// </summary>
[Collection("AuthIntegration")]
public class EventSettlementCreditEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
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

    // ============================ 1. Balance overlay after a partial per-share settle ============================

    [SkippableFact]
    public async Task GetBalance_AfterPartialPerShareSettle_ReflectsClearedAmountOutstandingAndSettlementStatus()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        // Bình's total net owed across the event is 300k + 200k = 500k.
        var expense1 = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 300_000m } }
        });
        await CreateExpenseAsync(client, new
        {
            name = "Cà phê", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 200_000m } }
        });
        var binhShare1Uuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(expense1)), binh));

        using (var settle = await client.PutAsJsonAsync(
                   $"api/v1/expenses/{Uuid(expense1)}/shares/{binhShare1Uuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settle.StatusCode);

        var balance = await GetBalanceDataAsync(client, evt);
        var binhRow = RowFor(balance, binh);
        Assert.Equal(300_000m, binhRow.GetProperty("clearedAmount").GetDecimal());
        Assert.Equal(200_000m, binhRow.GetProperty("outstanding").GetDecimal()); // 500k NetOwed - 300k cleared
        Assert.Equal("PartiallySettled", binhRow.GetProperty("settlementStatus").GetString());
        Assert.False(binhRow.GetProperty("isSettled").GetBoolean()); // ClearedAmount (300k) < NetOwed (500k)
        Assert.Equal(1, balance.GetProperty("partiallySettledMemberCount").GetInt32());
    }

    // ============================ 2. Event QR bills exactly the remaining amount after a partial credit ============================

    [SkippableFact]
    public async Task EventMemberQrs_PartialCredit_BillsExactlyRemainingAmount_ThenFullClearanceDropsMember_ThenAllCleared12003()
    {
        using var client = await CreatePremiumClientAsync(); // QR is Premium-gated (M10)
        await CreateBankAccountAsync(client);
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        // Bình's total net owed across the event is 300k + 200k = 500k.
        var expense1 = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 300_000m } }
        });
        var expense2 = await CreateExpenseAsync(client, new
        {
            name = "Cà phê", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 200_000m } }
        });
        await CloseEventAsync(client, evt);

        var binhShare1Uuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(expense1)), binh));
        var binhShare2Uuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(expense2)), binh));

        // Partial credit: settle only expense1's 300k share -> 200k remains outstanding.
        using (var settle = await client.PutAsJsonAsync(
                   $"api/v1/expenses/{Uuid(expense1)}/shares/{binhShare1Uuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settle.StatusCode);

        using (var members = await client.GetAsync($"api/v1/events/{evt}/qr/members"))
        {
            Assert.Equal(HttpStatusCode.OK, members.StatusCode);
            using var envelope = await ReadEnvelopeAsync(members);
            var data = envelope.RootElement.GetProperty("data");
            var only = Assert.Single(data.EnumerateArray());
            Assert.Equal(binh, only.GetProperty("memberUuid").GetString());
            Assert.Equal(200_000m, only.GetProperty("amount").GetDecimal()); // exactly the remainder, not the raw 500k balance
        }

        // Fully clear the remaining 200k -> Bình drops out of the QR entirely.
        using (var settle = await client.PutAsJsonAsync(
                   $"api/v1/expenses/{Uuid(expense2)}/shares/{binhShare2Uuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settle.StatusCode);

        using (var members = await client.GetAsync($"api/v1/events/{evt}/qr/members"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, members.StatusCode); // nobody left to bill -> 12003
            AssertErrorEnvelope(await ReadEnvelopeAsync(members), ErrorCodes.NoOutstandingDebtForQr);
        }
        using (var composite = await client.GetAsync($"api/v1/events/{evt}/qr"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, composite.StatusCode); // same for the binary composite QR
            AssertErrorEnvelope(await ReadEnvelopeAsync(composite), ErrorCodes.NoOutstandingDebtForQr);
        }
    }

    // ============================ 3. Cross-trigger consistency: whole-expense vs. per-share, end-to-end ============================

    [SkippableFact]
    public async Task WholeExpenseSettle_AndPerShareSettle_ProduceIdenticalResultingBalance_ForEquivalentSingleShareScenario()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);

        // Scenario A: whole-expense toggle.
        var binhA = await CreateMemberAsync(client, "Bình A");
        var evtA = await CreateEventUuidAsync(client, "Đợt A", Day14, Day16);
        var expenseA = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evtA,
            shares = new[] { new { memberUuid = binhA, amount = 500_000m } }
        });
        using (var settle = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(expenseA)}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settle.StatusCode);
        var balanceA = await GetBalanceDataAsync(client, evtA);
        var rowA = RowFor(balanceA, binhA);

        // Scenario B: per-share toggle on an equivalent single-share expense (same amount).
        var binhB = await CreateMemberAsync(client, "Bình B");
        var evtB = await CreateEventUuidAsync(client, "Đợt B", Day14, Day16);
        var expenseB = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evtB,
            shares = new[] { new { memberUuid = binhB, amount = 500_000m } }
        });
        var binhShareBUuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(expenseB)), binhB));
        using (var settle = await client.PutAsJsonAsync(
                   $"api/v1/expenses/{Uuid(expenseB)}/shares/{binhShareBUuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settle.StatusCode);
        var balanceB = await GetBalanceDataAsync(client, evtB);
        var rowB = RowFor(balanceB, binhB);

        Assert.Equal(rowA.GetProperty("clearedAmount").GetDecimal(), rowB.GetProperty("clearedAmount").GetDecimal());
        Assert.Equal(rowA.GetProperty("outstanding").GetDecimal(), rowB.GetProperty("outstanding").GetDecimal());
        Assert.Equal(rowA.GetProperty("settlementStatus").GetString(), rowB.GetProperty("settlementStatus").GetString());
        Assert.Equal(rowA.GetProperty("isSettled").GetBoolean(), rowB.GetProperty("isSettled").GetBoolean());
        Assert.Equal(500_000m, rowA.GetProperty("clearedAmount").GetDecimal());
        Assert.Equal("Settled", rowA.GetProperty("settlementStatus").GetString());
    }
}
