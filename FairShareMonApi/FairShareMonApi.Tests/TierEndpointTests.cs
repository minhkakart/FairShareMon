using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for M10 Free-tier create-limits, the Premium read-vs-mutation feature-gate
/// (OQ5b), the §4.9 over-limit guarantee, and the tier-on-token staleness contract - all against a test
/// host whose <c>Tiers:Free:</c> limits are overridden LOW (2/2/2) so a handful of rows hit the caps
/// (real MariaDB/Redis, skippable). A user's tier is set by flipping <c>users.tier</c> directly (no
/// upgrade endpoint exists) and re-logging in so the token carries it. Assertions target stable error
/// CODES; the Vietnamese message + interpolated number is checked on the member-limit case.
/// </summary>
[Collection("AuthIntegration")]
public class TierLimitEndpointTests(TierLimitWebApplicationFactory factory, DatabaseFixture fixture)
    : TierEndpointTestBase(factory, fixture), IClassFixture<TierLimitWebApplicationFactory>, IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;
    // Day-15 noon anchors: the +7 shift keeps them firmly inside their own calendar month (no boundary flip).
    private static readonly DateTime ThisMonth = new(Now.Year, Now.Month, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastMonth =
        new(Now.AddMonths(-1).Year, Now.AddMonths(-1).Month, 15, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<string> OwnerRepUuidAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/v1/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray()
            .Single(member => member.GetProperty("isOwnerRepresentative").GetBoolean())
            .GetProperty("uuid").GetString()!;
    }

    private static Task<HttpResponseMessage> PostMemberAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("api/v1/members", new { name });

    private static Task<HttpResponseMessage> PostEventAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("api/v1/events", new
        {
            name,
            startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        });

    private static Task<HttpResponseMessage> PostExpenseAsync(HttpClient client, string memberUuid, DateTime expenseTime) =>
        client.PostAsJsonAsync("api/v1/expenses", new
        {
            name = "Ăn trưa",
            expenseTime,
            shares = new[] { new { memberUuid, amount = 100_000m } }
        });

    // ---- Member limit (13000) ---------------------------------------------------------------------

    [SkippableFact]
    public async Task Member_FreeReachingLimit_Returns400Code13000WithVietnameseMessage()
    {
        var (client, _) = await CreateFreeClientAsync();
        // Owner-rep already occupies 1 of the 2 slots; the first API member fills the 2nd.
        using (var first = await PostMemberAsync(client, "An"))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var overLimit = await PostMemberAsync(client, "Bình");

        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
        using var envelope = await ReadEnvelopeAsync(overLimit);
        AssertErrorEnvelope(envelope, ErrorCodes.MemberLimitReached);
        var message = envelope.RootElement.GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("2", message);        // the interpolated configured limit
        Assert.Contains("Premium", message);  // names the upsell (Vietnamese message)
        client.Dispose();
    }

    [SkippableFact]
    public async Task Member_Premium_BypassesLimit()
    {
        using var client = await CreatePremiumClientAsync();

        for (var i = 0; i < 4; i++) // well past the Free cap of 2
        {
            using var response = await PostMemberAsync(client, $"Thành viên {i}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---- Open-event limit (13001) -----------------------------------------------------------------

    [SkippableFact]
    public async Task OpenEvent_FreeReachingLimit_Returns400Code13001_AndClosingFreesASlot()
    {
        var (client, _) = await CreateFreeClientAsync();
        using (var e1 = await PostEventAsync(client, "Đợt 1")) Assert.Equal(HttpStatusCode.OK, e1.StatusCode);
        string closableUuid;
        using (var e2 = await PostEventAsync(client, "Đợt 2"))
        {
            Assert.Equal(HttpStatusCode.OK, e2.StatusCode);
            using var env = await ReadEnvelopeAsync(e2);
            closableUuid = env.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
        }

        using (var e3 = await PostEventAsync(client, "Đợt 3"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, e3.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(e3), ErrorCodes.OpenEventLimitReached);
        }

        // Closing an open event frees a slot (only OPEN events count).
        using (var close = await client.PutAsync($"api/v1/events/{closableUuid}/close", null))
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        using var e4 = await PostEventAsync(client, "Đợt 4");
        Assert.Equal(HttpStatusCode.OK, e4.StatusCode); // slot freed by closing
        client.Dispose();
    }

    [SkippableFact]
    public async Task OpenEvent_Premium_BypassesLimit()
    {
        using var client = await CreatePremiumClientAsync();

        for (var i = 0; i < 4; i++)
        {
            using var response = await PostEventAsync(client, $"Đợt {i}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---- Monthly-expense limit (13002) ------------------------------------------------------------

    [SkippableFact]
    public async Task Expense_FreeReachingLimit_Returns400Code13002()
    {
        var (client, _) = await CreateFreeClientAsync();
        var owner = await OwnerRepUuidAsync(client);

        using (var x1 = await PostExpenseAsync(client, owner, ThisMonth)) Assert.Equal(HttpStatusCode.OK, x1.StatusCode);
        using (var x2 = await PostExpenseAsync(client, owner, ThisMonth)) Assert.Equal(HttpStatusCode.OK, x2.StatusCode);

        using var overLimit = await PostExpenseAsync(client, owner, ThisMonth);
        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(overLimit), ErrorCodes.MonthlyExpenseLimitReached);
        client.Dispose();
    }

    [SkippableFact]
    public async Task Expense_LastMonthDated_DoesNotCountTowardCurrentMonth()
    {
        var (client, _) = await CreateFreeClientAsync();
        var owner = await OwnerRepUuidAsync(client);

        // Three expenses dated LAST month all succeed - they never occupy a current-month slot.
        for (var i = 0; i < 3; i++)
        {
            using var backdated = await PostExpenseAsync(client, owner, LastMonth);
            Assert.Equal(HttpStatusCode.OK, backdated.StatusCode);
        }

        // The current month is still empty, so two current-month creates still succeed...
        using (var x1 = await PostExpenseAsync(client, owner, ThisMonth)) Assert.Equal(HttpStatusCode.OK, x1.StatusCode);
        using (var x2 = await PostExpenseAsync(client, owner, ThisMonth)) Assert.Equal(HttpStatusCode.OK, x2.StatusCode);

        // ...and only the third current-month create is blocked.
        using var overLimit = await PostExpenseAsync(client, owner, ThisMonth);
        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(overLimit), ErrorCodes.MonthlyExpenseLimitReached);
        client.Dispose();
    }

    [SkippableFact]
    public async Task Expense_Premium_BypassesLimit()
    {
        using var client = await CreatePremiumClientAsync();
        var owner = await OwnerRepUuidAsync(client);

        for (var i = 0; i < 4; i++)
        {
            using var response = await PostExpenseAsync(client, owner, ThisMonth);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---- §4.9: an over-limit Free user can still read / edit / delete existing data ----------------

    [SkippableFact]
    public async Task OverLimitFreeUser_CanReadEditDeleteExistingData_OnlyCreateBlocked()
    {
        var (client, username) = await CreateFreeClientAsync();
        var user = await GetUserAsync(username);
        var ownerRep = await GetOwnerRepAsync(user.Id);
        var category = await GetDefaultCategoryAsync(user.Id);

        // Push the user OVER every limit by seeding directly (bypasses the create-guard).
        var memberA = await SeedMemberAsync(user.Id, "Thừa A");
        var memberB = await SeedMemberAsync(user.Id, "Thừa B");
        await SeedMemberAsync(user.Id, "Thừa C"); // owner-rep + 3 = 4 > 2
        var eventA = await SeedOpenEventAsync(user.Id, "Đợt A");
        var eventB = await SeedOpenEventAsync(user.Id, "Đợt B");
        await SeedOpenEventAsync(user.Id, "Đợt C"); // 3 open > 2
        var expenseA = await SeedExpenseAsync(user.Id, ownerRep.Id, category.Id, ThisMonth);
        var expenseB = await SeedExpenseAsync(user.Id, ownerRep.Id, category.Id, ThisMonth);
        await SeedExpenseAsync(user.Id, ownerRep.Id, category.Id, ThisMonth); // 3 this month > 2

        // CREATE is blocked on all three resources.
        using (var m = await PostMemberAsync(client, "Mới")) { Assert.Equal(HttpStatusCode.BadRequest, m.StatusCode); AssertErrorEnvelope(await ReadEnvelopeAsync(m), ErrorCodes.MemberLimitReached); }
        using (var e = await PostEventAsync(client, "Mới")) { Assert.Equal(HttpStatusCode.BadRequest, e.StatusCode); AssertErrorEnvelope(await ReadEnvelopeAsync(e), ErrorCodes.OpenEventLimitReached); }
        using (var x = await PostExpenseAsync(client, ownerRep.Uuid, ThisMonth)) { Assert.Equal(HttpStatusCode.BadRequest, x.StatusCode); AssertErrorEnvelope(await ReadEnvelopeAsync(x), ErrorCodes.MonthlyExpenseLimitReached); }

        // READ lists still work (nothing hidden).
        using (var members = await client.GetAsync("api/v1/members")) Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        using (var events = await client.GetAsync("api/v1/events")) Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        using (var expenses = await client.GetAsync("api/v1/expenses")) Assert.Equal(HttpStatusCode.OK, expenses.StatusCode);

        // EDIT existing rows still works.
        using (var rename = await client.PutAsJsonAsync($"api/v1/members/{memberA.Uuid}", new { name = "Đổi tên" }))
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        using (var editEvent = await client.PutAsJsonAsync($"api/v1/events/{eventA.Uuid}",
            new { name = "Đợt A sửa", startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), endDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc) }))
            Assert.Equal(HttpStatusCode.OK, editEvent.StatusCode);
        using (var closeEvent = await client.PutAsync($"api/v1/events/{eventB.Uuid}/close", null))
            Assert.Equal(HttpStatusCode.OK, closeEvent.StatusCode);
        using (var editExpense = await client.PutAsJsonAsync($"api/v1/expenses/{expenseA.Uuid}", new { name = "Sửa phiếu", expenseTime = ThisMonth }))
            Assert.Equal(HttpStatusCode.OK, editExpense.StatusCode);
        using (var settled = await client.PutAsJsonAsync($"api/v1/expenses/{expenseA.Uuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, settled.StatusCode);

        // DELETE existing rows still works.
        using (var delMember = await client.DeleteAsync($"api/v1/members/{memberB.Uuid}")) Assert.Equal(HttpStatusCode.OK, delMember.StatusCode);
        using (var delExpense = await client.DeleteAsync($"api/v1/expenses/{expenseB.Uuid}")) Assert.Equal(HttpStatusCode.OK, delExpense.StatusCode);
        client.Dispose();
    }

    // ---- Premium feature-gate: read-vs-mutation split (OQ5b) --------------------------------------

    [SkippableFact]
    public async Task Free_WalletMutations_Return403Code13003()
    {
        var (client, _) = await CreateFreeClientAsync();

        using (var create = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(create), ErrorCodes.PremiumFeatureRequired);
        }

        // The gate fires before resource resolution, so a dummy uuid still returns 403 (not 404).
        using (var update = await client.PutAsJsonAsync("api/v1/bank-accounts/some-uuid",
            new { bankBin = "970436", bankName = "X", accountNumber = "0123456789", accountHolderName = "Y" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(update), ErrorCodes.PremiumFeatureRequired);
        }

        using (var setDefault = await client.PutAsync("api/v1/bank-accounts/some-uuid/default", null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, setDefault.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(setDefault), ErrorCodes.PremiumFeatureRequired);
        }

        using (var delete = await client.DeleteAsync("api/v1/bank-accounts/some-uuid"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(delete), ErrorCodes.PremiumFeatureRequired);
        }
        client.Dispose();
    }

    [SkippableFact]
    public async Task Free_WalletReads_StayOpen_Return200()
    {
        var (client, username) = await CreateFreeClientAsync();
        var user = await GetUserAsync(username);
        // Simulate an account added while Premium, then a downgrade to Free: the read must still work.
        var account = await SeedBankAccountAsync(user.Id);

        using (var list = await client.GetAsync("api/v1/bank-accounts"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var env = await ReadEnvelopeAsync(list);
            Assert.True(env.RootElement.GetProperty("isSuccess").GetBoolean());
            Assert.Single(env.RootElement.GetProperty("data").EnumerateArray());
        }

        using (var get = await client.GetAsync($"api/v1/bank-accounts/{account.Uuid}"))
            Assert.Equal(HttpStatusCode.OK, get.StatusCode); // get read is not gated (OQ5b)
        client.Dispose();
    }

    [SkippableFact]
    public async Task Free_QrRoutes_Return403Code13003()
    {
        var (client, _) = await CreateFreeClientAsync();

        using (var expenseQr = await client.GetAsync("api/v1/expenses/some-uuid/qr"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, expenseQr.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(expenseQr), ErrorCodes.PremiumFeatureRequired);
        }

        using (var eventQr = await client.GetAsync("api/v1/events/some-uuid/qr"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, eventQr.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(eventQr), ErrorCodes.PremiumFeatureRequired);
        }
        client.Dispose();
    }

    [SkippableFact]
    public async Task Free_QrMemberRoutes_Return403Code13003()
    {
        var (client, _) = await CreateFreeClientAsync();

        using (var expenseQr = await client.GetAsync("api/v1/expenses/some-uuid/qr/members"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, expenseQr.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(expenseQr), ErrorCodes.PremiumFeatureRequired);
        }

        using (var eventQr = await client.GetAsync("api/v1/events/some-uuid/qr/members"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, eventQr.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(eventQr), ErrorCodes.PremiumFeatureRequired);
        }
        client.Dispose();
    }

    [SkippableFact]
    public async Task Free_CsvExport_StaysFree_Returns200()
    {
        var (client, _) = await CreateFreeClientAsync();
        var owner = await OwnerRepUuidAsync(client);
        string expenseUuid;
        using (var create = await PostExpenseAsync(client, owner, ThisMonth)) // 1 of 2 - within the Free cap
        {
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            using var env = await ReadEnvelopeAsync(create);
            expenseUuid = env.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
        }

        using var export = await client.GetAsync($"api/v1/expenses/{expenseUuid}/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode); // CSV export is NOT Premium-gated
        client.Dispose();
    }

    [SkippableFact]
    public async Task Premium_WalletMutationAndExpenseQr_Allowed()
    {
        using var client = await CreatePremiumClientAsync();

        using (var create = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" }))
            Assert.Equal(HttpStatusCode.OK, create.StatusCode); // Premium wallet mutation allowed

        // Bình (non-payer) holds the billable share so the per-member QR has someone to bill: the payer
        // defaults to the owner-rep, whose own share is never billed.
        string binh;
        using (var member = await PostMemberAsync(client, "Bình"))
        {
            Assert.Equal(HttpStatusCode.OK, member.StatusCode);
            using var env = await ReadEnvelopeAsync(member);
            binh = env.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
        }

        string expenseUuid;
        using (var expense = await PostExpenseAsync(client, binh, ThisMonth))
        {
            Assert.Equal(HttpStatusCode.OK, expense.StatusCode);
            using var env = await ReadEnvelopeAsync(expense);
            expenseUuid = env.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
        }

        using var qr = await client.GetAsync($"api/v1/expenses/{expenseUuid}/qr");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode); // Premium QR allowed
        Assert.Equal("image/png", qr.Content.Headers.ContentType!.ToString());
    }

    // ---- Tier rides the token (staleness contract, OQ8a) ------------------------------------------

    [SkippableFact]
    public async Task Tier_FlipToPremium_ReflectsOnlyAfterReLogin()
    {
        var (freeClient, username) = await CreateFreeClientAsync();

        // Free token -> wallet mutation gated.
        using (var gated = await freeClient.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" }))
            Assert.Equal(HttpStatusCode.Forbidden, gated.StatusCode);

        // Flip the DB tier to Premium. The ALREADY-ISSUED token keeps FREE (captured at login).
        await SetUserTierAsync(username, UserTiers.Premium);
        using (var stillGated = await freeClient.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" }))
            Assert.Equal(HttpStatusCode.Forbidden, stillGated.StatusCode); // staleness contract

        // A fresh login issues a token carrying PREMIUM -> the gate lifts.
        using var premiumClient = await LoginClientAsync(username);
        using var allowed = await premiumClient.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        freeClient.Dispose();
    }
}

/// <summary>
/// Proves the owner-representative bootstrap is exempt from the M10 member create-guard: with
/// <c>Tiers:Free:MaxMembers = 0</c>, registration still auto-creates the one owner-rep member (the
/// bootstrap uses a different seam, not the guarded <c>MembersService.CreateAsync</c>), while the
/// guarded <c>POST /members</c> is blocked at 0.
/// </summary>
[Collection("AuthIntegration")]
public class OwnerRepExemptionEndpointTests(ZeroMemberLimitWebApplicationFactory factory, DatabaseFixture fixture)
    : TierEndpointTestBase(factory, fixture), IClassFixture<ZeroMemberLimitWebApplicationFactory>, IClassFixture<DatabaseFixture>
{
    [SkippableFact]
    public async Task Register_WithZeroMemberLimit_StillYieldsOwnerRep_ButGuardedCreateBlocked()
    {
        var (client, _) = await CreateFreeClientAsync();

        using (var members = await client.GetAsync("api/v1/members"))
        {
            Assert.Equal(HttpStatusCode.OK, members.StatusCode);
            using var envelope = await ReadEnvelopeAsync(members);
            var member = Assert.Single(envelope.RootElement.GetProperty("data").EnumerateArray());
            Assert.True(member.GetProperty("isOwnerRepresentative").GetBoolean()); // bootstrap bypassed the guard
        }

        using var create = await client.PostAsJsonAsync("api/v1/members", new { name = "An" });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(create), ErrorCodes.MemberLimitReached); // guard active at 0
        client.Dispose();
    }
}
