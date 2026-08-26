using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
/// End-to-end HTTP tests for the bank-callback webhook receiver and owner-facing review list
/// (planning/bank-callback-settlement.md Step 10) via WebApplicationFactory (real MariaDB/Redis -
/// skippable). Proves full webhook-&gt;settle PARITY against the existing manual
/// <c>PUT .../settled</c> routes for BOTH a <c>Share</c> target (incl. Direction 2's event-credit
/// cascade) and an <c>EventMember</c> target; idempotency (duplicate webhook -&gt; no double-credit);
/// wrong API key -&gt; 401 18000; unknown provider -&gt; 404 18002; malformed body -&gt; 400 18001; an
/// amount mismatch is ack'd 200 but held back and visible via <c>GET /api/v1/bank-callbacks</c>; the
/// review endpoint is authenticated-only (anonymous -&gt; 401); and a fully-unmatched code is recorded
/// with <c>ResolvedUserId = null</c> - invisible to every owner's list (OQ5's known trade-off).
/// </summary>
[Collection("AuthIntegration")]
public class BankCallbacksEndpointTests(BankCallbacksWebApplicationFactory factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture), IClassFixture<BankCallbacksWebApplicationFactory>, IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    // Every provider-transaction id this class sends is prefixed so DisposeAsync's sweep can find rows
    // that carry NO FK back to any prefix'd user (e.g. a fully-unmatched-code row - ResolvedUserId null).
    private readonly string _txPrefix = "sepaytx" + Guid.NewGuid().ToString("N")[..10] + "_";
    private long _txCounter;

    private string NextProviderTransactionId() => _txPrefix + Interlocked.Increment(ref _txCounter);

    private static async Task CreateBankAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    private static JsonElement RowFor(JsonElement balance, string memberUuid) =>
        balance.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("memberUuid").GetString() == memberUuid);

    /// <summary>Reads the DB directly for the correlation code embedded for a Share-target billed member (bypasses image/QR decoding - the memo itself is not exposed over JSON).</summary>
    private async Task<string> GetShareCorrelationCodeAsync(string expenseUuid, string memberUuid)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = await context.QrCorrelationCodes.AsNoTracking()
            .Include(c => c.Expense).Include(c => c.Member)
            .Where(c => c.Expense != null && c.Expense.Uuid == expenseUuid && c.Member.Uuid == memberUuid)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(code);
        return code!.Code;
    }

    private async Task<string> GetEventCorrelationCodeAsync(string eventUuid, string memberUuid)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = await context.QrCorrelationCodes.AsNoTracking()
            .Include(c => c.Event).Include(c => c.Member)
            .Where(c => c.Event != null && c.Event.Uuid == eventUuid && c.Member.Uuid == memberUuid)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(code);
        return code!.Code;
    }

    private static StringContent SePayBody(string providerTransactionId, string content, decimal amount, string transferType = "in") =>
        new(JsonSerializer.Serialize(new
        {
            id = providerTransactionId,
            gateway = "Vietcombank",
            transactionDate = "2026-08-26 14:02:37",
            accountNumber = "0123499999",
            code = (string?)null,
            content,
            transferType,
            transferAmount = amount,
            accumulated = 19_077_000,
            subAccount = (string?)null,
            referenceCode = "MBVCB.3278907687",
            description = ""
        }), Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> PostWebhookAsync(string providerTransactionId, string content, decimal amount, string apiKey = BankCallbacksWebApplicationFactory.ConfiguredApiKey, string transferType = "in")
    {
        using var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/bank-callbacks/sepay")
        {
            Content = SePayBody(providerTransactionId, content, amount, transferType)
        };
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", apiKey);
        return await client.SendAsync(request);
    }

    // ==================== Share target: full webhook -> settle parity, incl. Direction 2 ====================

    [SkippableFact]
    public async Task Webhook_MatchingShareTarget_Applies_SettlesShareAndFiresDirection2CreditCascade()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16); // event-scoped, so Direction 2 applies
        var expense = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 200_000m } }
        });
        var expenseUuid = Uuid(expense);

        // Trigger correlation-code embedding via the per-member QR route (Step 6).
        using (var qrResponse = await client.GetAsync($"api/v1/expenses/{expenseUuid}/qr/members"))
            Assert.Equal(HttpStatusCode.OK, qrResponse.StatusCode);
        var code = await GetShareCorrelationCodeAsync(expenseUuid, binh);

        var balanceBefore = RowFor(await GetBalanceDataAsync(client, evt), binh);
        Assert.Equal(0m, balanceBefore.GetProperty("clearedAmount").GetDecimal());

        using var webhook = await PostWebhookAsync(NextProviderTransactionId(), $"{code} chuyen tien", 200_000m);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        using var webhookEnvelope = await ReadEnvelopeAsync(webhook);
        Assert.True(webhookEnvelope.RootElement.GetProperty("isSuccess").GetBoolean());

        var refreshedExpense = await GetExpenseAsync(client, expenseUuid);
        Assert.True(ShareForMember(refreshedExpense, binh).GetProperty("isSettled").GetBoolean());

        // Direction 2 parity: exactly the same event-credit cascade as PUT .../shares/{shareUuid}/settled.
        var balanceAfter = RowFor(await GetBalanceDataAsync(client, evt), binh);
        Assert.Equal(200_000m, balanceAfter.GetProperty("clearedAmount").GetDecimal());
    }

    [SkippableFact]
    public async Task Webhook_DuplicateWebhookSameId_AckedTwice_NoDoubleCredit()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var expense = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 200_000m } }
        });
        var expenseUuid = Uuid(expense);
        using (var qrResponse = await client.GetAsync($"api/v1/expenses/{expenseUuid}/qr/members"))
            Assert.Equal(HttpStatusCode.OK, qrResponse.StatusCode);
        var code = await GetShareCorrelationCodeAsync(expenseUuid, binh);
        var providerTransactionId = NextProviderTransactionId();

        using (var first = await PostWebhookAsync(providerTransactionId, $"{code} chuyen tien", 200_000m))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var clearedAfterFirst = RowFor(await GetBalanceDataAsync(client, evt), binh).GetProperty("clearedAmount").GetDecimal();

        using (var second = await PostWebhookAsync(providerTransactionId, $"{code} chuyen tien", 200_000m)) // identical id
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var clearedAfterSecond = RowFor(await GetBalanceDataAsync(client, evt), binh).GetProperty("clearedAmount").GetDecimal();

        Assert.Equal(200_000m, clearedAfterFirst);
        Assert.Equal(clearedAfterFirst, clearedAfterSecond); // no double-credit
    }

    // ==================== EventMember target: full webhook -> settle parity ====================

    [SkippableFact]
    public async Task Webhook_MatchingEventMemberTarget_Applies_SettlesMembersEventBalance()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối", expenseTime = Day15Noon, payerMemberUuid = an, eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 300_000m } }
        });
        await CloseEventAsync(client, evt); // event QR is closed-only

        using (var qrResponse = await client.GetAsync($"api/v1/events/{evt}/qr/members"))
            Assert.Equal(HttpStatusCode.OK, qrResponse.StatusCode);
        var code = await GetEventCorrelationCodeAsync(evt, binh);

        using var webhook = await PostWebhookAsync(NextProviderTransactionId(), $"{code} chuyen tien", 300_000m);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);

        var row = RowFor(await GetBalanceDataAsync(client, evt), binh);
        Assert.True(row.GetProperty("isSettled").GetBoolean()); // parity with PUT .../members/{m}/settled
        Assert.Equal(0m, row.GetProperty("outstanding").GetDecimal());
    }

    // ==================== Auth / provider / payload validation ====================

    [SkippableFact]
    public async Task Webhook_WrongApiKey_Returns401Code18000()
    {
        using var response = await PostWebhookAsync(NextProviderTransactionId(), "FSMNOTREAL chuyen tien", 100_000m, apiKey: "wrong-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankCallbackVerificationFailed);
    }

    [SkippableFact]
    public async Task Webhook_MissingApiKeyHeader_Returns401Code18000()
    {
        using var response = await PostWebhookAsync(NextProviderTransactionId(), "FSMNOTREAL chuyen tien", 100_000m, apiKey: "");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankCallbackVerificationFailed);
    }

    [SkippableFact]
    public async Task Webhook_UnknownProviderSegment_Returns404Code18002()
    {
        using var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/bank-callbacks/unknownbank")
        {
            Content = SePayBody(NextProviderTransactionId(), "FSMNOTREAL chuyen tien", 100_000m)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", BankCallbacksWebApplicationFactory.ConfiguredApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankCallbackProviderUnknown);
    }

    [SkippableFact]
    public async Task Webhook_MalformedBody_MissingIdField_Returns400Code18001()
    {
        using var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/bank-callbacks/sepay")
        {
            Content = new StringContent("""{"transferType":"in","transferAmount":100000,"content":"hi"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", BankCallbacksWebApplicationFactory.ConfiguredApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankCallbackPayloadInvalid);
    }

    // ==================== Amount mismatch: ack'd but held back, visible via the review list ====================

    [SkippableFact]
    public async Task Webhook_AmountMismatch_Returns200ButShareStaysUnsettled_VisibleInReviewListAsAmountMismatch()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var expense = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa", expenseTime = Day15Noon, payerMemberUuid = an,
            shares = new[] { new { memberUuid = binh, amount = 200_000m } }
        });
        var expenseUuid = Uuid(expense);
        using (var qrResponse = await client.GetAsync($"api/v1/expenses/{expenseUuid}/qr/members"))
            Assert.Equal(HttpStatusCode.OK, qrResponse.StatusCode);
        var code = await GetShareCorrelationCodeAsync(expenseUuid, binh);

        using var webhook = await PostWebhookAsync(NextProviderTransactionId(), $"{code} chuyen tien", 199_999m); // wrong amount
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode); // still ack'd - a webhook isn't "in error" for this

        var refreshedExpense = await GetExpenseAsync(client, expenseUuid);
        Assert.False(ShareForMember(refreshedExpense, binh).GetProperty("isSettled").GetBoolean());

        using var listResponse = await client.GetAsync("api/v1/bank-callbacks");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listEnvelope = await ReadEnvelopeAsync(listResponse);
        var row = listEnvelope.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("content").GetString() == $"{code} chuyen tien");
        Assert.Equal("AmountMismatch", row.GetProperty("outcome").GetString());
    }

    // ==================== Review list: authenticated only, ungated ====================

    [SkippableFact]
    public async Task ReviewList_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/bank-callbacks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // authenticated-only - NOT the anonymous route
    }

    // ==================== Fully-unmatched code: recorded but invisible to every owner ====================

    [SkippableFact]
    public async Task Webhook_UnmatchedCode_GarbageContent_RecordedWithNullResolvedUser_InvisibleToAnyOwnersList()
    {
        using var owner = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(owner);
        var providerTransactionId = NextProviderTransactionId();

        using var webhook = await PostWebhookAsync(providerTransactionId, "khong co ma lien ket gi ca", 100_000m);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode); // still ack'd 200

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await context.BankTransactionCallbacks.AsNoTracking()
            .SingleAsync(c => c.ProviderTransactionId == providerTransactionId);
        Assert.Null(row.ResolvedUserId);
        Assert.Equal("UnmatchedCode", row.Outcome.ToString());

        using var listResponse = await owner.GetAsync("api/v1/bank-callbacks");
        using var listEnvelope = await ReadEnvelopeAsync(listResponse);
        Assert.DoesNotContain(listEnvelope.RootElement.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("content").GetString() == "khong co ma lien ket gi ca");
    }

    // Defensive cleanup: sweep the two new bank-callback-settlement tables BEFORE the base class deletes
    // the prefix's users - qr_correlation_codes.member_id is RESTRICT (mirrors EventMemberSettlement.
    // MemberId); bank_transaction_callbacks rows with no resolved user (UnmatchedCode) carry no FK to any
    // prefix'd user at all, so they are swept by this class's own provider-transaction-id prefix instead.
    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await context.BankTransactionCallbacks
                .Where(callback => callback.ProviderTransactionId.StartsWith(_txPrefix))
                .ExecuteDeleteAsync();

            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();
            await context.QrCorrelationCodes.Where(code => userIds.Contains(code.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
