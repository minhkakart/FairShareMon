using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the M11 admin suite via <see cref="WebApplicationFactory{Program}"/> (real
/// MariaDB/Redis, skippable). Covers the authorization matrix on EVERY <c>/admin/*</c> route (anonymous
/// -> 401; FREE and PREMIUM non-admins -> 403 <c>Forbidden 1004</c>; admin allowed), grant/revoke with
/// the Redis cache-bust (the target's PRE-EXISTING token reflects the new tier on its next request with
/// NO re-login), disable/enable + the disabled-login block, reset-password, role promote/demote, the
/// self/other-admin guards (14001/14002), the dashboards, and - the headline safety test - the §4.1/R10
/// PRIVACY BOUNDARY: no admin response exposes any user's ledger data. Assertions target stable error
/// CODES. Cache-bust/role-reflection tests additionally skip when Redis is unreachable.
/// </summary>
[Collection("AuthIntegration")]
public class AdminEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture, RedisFixture redis)
    : AdminEndpointTestBase(factory, fixture),
      IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _redis = redis;

    // Every admin route (method + path). {uuid} is a dummy - authorization runs before model binding,
    // so a non-admin / anonymous caller is rejected regardless of the (missing) body or the uuid.
    private static readonly (string Method, string Path)[] AdminRoutes =
    [
        ("GET", "api/v1/admin/dashboard"),
        ("GET", "api/v1/admin/revenue"),
        ("GET", "api/v1/admin/users"),
        ("GET", "api/v1/admin/users/some-uuid"),
        ("POST", "api/v1/admin/users/some-uuid/tier/grant"),
        ("POST", "api/v1/admin/users/some-uuid/tier/revoke"),
        ("POST", "api/v1/admin/users/some-uuid/disable"),
        ("POST", "api/v1/admin/users/some-uuid/enable"),
        ("POST", "api/v1/admin/users/some-uuid/revoke-tokens"),
        ("POST", "api/v1/admin/users/some-uuid/reset-password"),
        ("POST", "api/v1/admin/users/some-uuid/role")
    ];

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path) =>
        method == "GET" ? client.GetAsync(path) : client.PostAsJsonAsync(path, new { });

    private static Task<HttpResponseMessage> BankAccountMutationAsync(HttpClient client) =>
        client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });

    // ---- Authorization matrix -----------------------------------------------------------------------

    [SkippableFact]
    public async Task AllAdminRoutes_Anonymous_Return401()
    {
        Fixture.SkipIfNoDb();
        using var anonymous = Factory.CreateClient();

        foreach (var (method, path) in AdminRoutes)
        {
            using var response = await SendAsync(anonymous, method, path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [SkippableFact]
    public async Task AllAdminRoutes_FreeUser_Return403Forbidden1004()
    {
        Fixture.SkipIfNoDb();
        var (client, _) = await CreateFreeClientAsync();

        foreach (var (method, path) in AdminRoutes)
        {
            using var response = await SendAsync(client, method, path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Forbidden);
        }

        client.Dispose();
    }

    [SkippableFact]
    public async Task AllAdminRoutes_PremiumUser_Return403Forbidden1004()
    {
        Fixture.SkipIfNoDb();
        var (client, _) = await CreatePremiumClientAsync();

        foreach (var (method, path) in AdminRoutes)
        {
            using var response = await SendAsync(client, method, path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Forbidden);
        }

        client.Dispose();
    }

    [SkippableFact]
    public async Task AdminUser_CanReachDashboardRevenueAndUsers()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();

        using (var dashboard = await admin.GetAsync("api/v1/admin/dashboard"))
            Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        using (var revenue = await admin.GetAsync("api/v1/admin/revenue"))
            Assert.Equal(HttpStatusCode.OK, revenue.StatusCode);
        using (var users = await admin.GetAsync("api/v1/admin/users"))
            Assert.Equal(HttpStatusCode.OK, users.StatusCode);

        admin.Dispose();
    }

    [SkippableFact]
    public async Task GetUser_UnknownUuid_Returns404Code14000()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();

        using var response = await admin.GetAsync("api/v1/admin/users/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.AdminUserNotFound);
        admin.Dispose();
    }

    // ---- Grant / revoke + cache-bust (no re-login) --------------------------------------------------

    [SkippableFact]
    public async Task Grant_MakesFreeUserPremium_ReflectsOnPreExistingTokenWithoutReLogin_RevokeReverts()
    {
        Fixture.SkipIfNoDb();
        _redis.SkipIfNoRedis(); // the cache-bust depends on a live Redis holding the cached token entry

        var (admin, _, _) = await CreateAdminClientAsync();
        var (targetClient, targetUsername) = await CreateFreeClientAsync(); // token issued while FREE
        var target = await GetUserAsync(targetUsername);

        // The FREE token is gated out of the Premium wallet feature.
        using (var gated = await BankAccountMutationAsync(targetClient))
            Assert.Equal(HttpStatusCode.Forbidden, gated.StatusCode);

        // Admin grants Premium.
        using (var grant = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/tier/grant",
            new { amount = 199_000m, reference = "TT-CACHEBUST" }))
        {
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
            using var env = await ReadEnvelopeAsync(grant);
            Assert.Equal(TierGrantActions.Grant, env.RootElement.GetProperty("data").GetProperty("action").GetString());
        }

        Assert.Equal(UserTiers.Premium, (await GetUserAsync(targetUsername)).Tier);
        Assert.Equal(1, await CountGrantsAsync(target.Id, TierGrantActions.Grant));

        // Cache-bust (OQ3a): the SAME pre-existing FREE token now passes the Premium gate on its NEXT
        // request, with NO re-login.
        using (var allowed = await BankAccountMutationAsync(targetClient))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // Admin revokes -> back to Free -> the gate closes again on the next request.
        using (var revoke = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/tier/revoke", new { note = "hết hạn" }))
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        Assert.Equal(UserTiers.Free, (await GetUserAsync(targetUsername)).Tier);
        Assert.Equal(1, await CountGrantsAsync(target.Id, TierGrantActions.Revoke));

        using (var gatedAgain = await BankAccountMutationAsync(targetClient))
            Assert.Equal(HttpStatusCode.Forbidden, gatedAgain.StatusCode);

        admin.Dispose();
        targetClient.Dispose();
    }

    [SkippableFact]
    public async Task Grant_NegativeAmount_Returns400ValidationFailed()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var (_, targetUsername) = await CreateFreeClientAsync();
        var target = await GetUserAsync(targetUsername);

        using var response = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/tier/grant", new { amount = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ValidationFailed);
        admin.Dispose();
    }

    // ---- Disable / enable ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task Disable_RevokesExistingTokens_AndBlocksLogin_EnableRestoresLogin()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var (targetClient, targetUsername) = await CreateFreeClientAsync();
        var target = await GetUserAsync(targetUsername);

        // The target's token works before disable.
        using (var ok = await targetClient.GetAsync("api/v1/members"))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using (var disable = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/disable", new { }))
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        Assert.Equal(UserStatuses.Disabled, (await GetUserAsync(targetUsername)).Status);

        // Existing token is dead (revoked on disable) -> 401.
        using (var afterDisable = await targetClient.GetAsync("api/v1/members"))
            Assert.Equal(HttpStatusCode.Unauthorized, afterDisable.StatusCode);

        // Login is blocked with 14003 (mapped to HTTP 403).
        using (var blockedLogin = await RawLoginAsync(targetUsername))
        {
            Assert.Equal(HttpStatusCode.Forbidden, blockedLogin.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(blockedLogin), ErrorCodes.AccountDisabled);
        }

        // Enable restores login.
        using (var enable = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/enable", new { }))
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.Equal(UserStatuses.Active, (await GetUserAsync(targetUsername)).Status);

        using (var goodLogin = await RawLoginAsync(targetUsername))
            Assert.Equal(HttpStatusCode.OK, goodLogin.StatusCode);

        admin.Dispose();
        targetClient.Dispose();
    }

    // ---- Reset password -----------------------------------------------------------------------------

    [SkippableFact]
    public async Task ResetPassword_ReturnsTempPasswordOnce_KillsOldTokens_NewPasswordLogsIn()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var (targetClient, targetUsername) = await CreateFreeClientAsync();
        var target = await GetUserAsync(targetUsername);
        const string newPassword = "reset-password-9+";

        using (var reset = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/reset-password", new { newPassword }))
        {
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            using var env = await ReadEnvelopeAsync(reset);
            var data = env.RootElement.GetProperty("data");
            Assert.Equal(newPassword, data.GetProperty("password").GetString()); // temp password returned once
            Assert.Equal(targetUsername, data.GetProperty("username").GetString());
        }

        // Old tokens are dead.
        using (var afterReset = await targetClient.GetAsync("api/v1/members"))
            Assert.Equal(HttpStatusCode.Unauthorized, afterReset.StatusCode);

        // The OLD password no longer logs in; the NEW one does.
        using (var oldLogin = await RawLoginAsync(targetUsername, Password))
            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using (var newLogin = await RawLoginAsync(targetUsername, newPassword))
            Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        admin.Dispose();
        targetClient.Dispose();
    }

    // ---- Role promote / demote ----------------------------------------------------------------------

    [SkippableFact]
    public async Task Role_PromoteReachesAdmin_OnPreExistingTokenViaCacheBust_DemotingThatAdminIsGuarded()
    {
        Fixture.SkipIfNoDb();
        _redis.SkipIfNoRedis(); // role rides the token like tier; the cache-bust needs a live Redis

        var (admin, _, _) = await CreateAdminClientAsync();
        var (targetClient, targetUsername) = await CreateFreeClientAsync(); // USER token
        var target = await GetUserAsync(targetUsername);

        // A plain USER cannot reach admin routes.
        using (var denied = await targetClient.GetAsync("api/v1/admin/dashboard"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Promote -> the SAME pre-existing token reaches the admin route on its next request (cache-bust,
        // no re-login). Promotion is unguarded.
        using (var promote = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/role", new { role = UserRoles.Admin }))
            Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        Assert.Equal(UserRoles.Admin, (await GetUserAsync(targetUsername)).Role);
        using (var reachable = await targetClient.GetAsync("api/v1/admin/dashboard"))
            Assert.Equal(HttpStatusCode.OK, reachable.StatusCode);

        // Demoting the now-admin target is BLOCKED (14002): per OQ10(a) an admin can never demote
        // another admin via the API (admins are removed via DB/config), which is also the last-admin
        // guarantee. So role demotion of an admin is intentionally impossible over HTTP.
        using (var demote = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/role", new { role = UserRoles.User }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
            AssertErrorEnvelope(await ReadEnvelopeAsync(demote), ErrorCodes.AdminCannotTargetAdmin);
        }
        Assert.Equal(UserRoles.Admin, (await GetUserAsync(targetUsername)).Role); // still admin - demote rejected

        admin.Dispose();
        targetClient.Dispose();
    }

    // ---- Self / other-admin guards (14001 / 14002) --------------------------------------------------

    [SkippableFact]
    public async Task SelfDisable_Returns400Code14001()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, adminUuid) = await CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync($"api/v1/admin/users/{adminUuid}/disable", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.AdminCannotTargetSelf);
        admin.Dispose();
    }

    [SkippableFact]
    public async Task SelfDemote_Returns400Code14001()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, adminUuid) = await CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync($"api/v1/admin/users/{adminUuid}/role", new { role = UserRoles.User });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.AdminCannotTargetSelf);
        admin.Dispose();
    }

    [SkippableFact]
    public async Task DisableAnotherAdmin_Returns400Code14002()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var otherAdminUsername = NewUsername();
        await RegisterAsync(otherAdminUsername);
        await SetRoleTierStatusAsync(otherAdminUsername, role: UserRoles.Admin);
        var otherAdmin = await GetUserAsync(otherAdminUsername);

        using var response = await admin.PostAsJsonAsync($"api/v1/admin/users/{otherAdmin.Uuid}/disable", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.AdminCannotTargetAdmin);
        admin.Dispose();
    }

    [SkippableFact]
    public async Task DemoteAnotherAdmin_TheLastAdminGuard_Returns400Code14002()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var otherAdminUsername = NewUsername();
        await RegisterAsync(otherAdminUsername);
        await SetRoleTierStatusAsync(otherAdminUsername, role: UserRoles.Admin);
        var otherAdmin = await GetUserAsync(otherAdminUsername);

        // Demoting ANOTHER admin is blocked (14002), which is exactly what prevents the system from ever
        // reaching zero admins (the last-admin guarantee).
        using var response = await admin.PostAsJsonAsync($"api/v1/admin/users/{otherAdmin.Uuid}/role", new { role = UserRoles.User });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.AdminCannotTargetAdmin);
        admin.Dispose();
    }

    // ---- Dashboards ---------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Dashboard_ReturnsDistributionsAndSignupBuckets()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();

        using var response = await admin.GetAsync("api/v1/admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var env = await ReadEnvelopeAsync(response);
        var data = env.RootElement.GetProperty("data");
        Assert.True(env.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.True(data.GetProperty("totalUsers").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("tierDistribution").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("roleDistribution").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("statusDistribution").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("signups").ValueKind);
        admin.Dispose();
    }

    [SkippableFact]
    public async Task Revenue_SumsGrantAmounts_WithBucketsAndReferences()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var (_, targetUsername) = await CreateFreeClientAsync();
        var target = await GetUserAsync(targetUsername);
        var reference = "REV-" + Guid.NewGuid().ToString("N")[..8];

        using (var grant = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/tier/grant",
            new { amount = 249_000m, reference }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var from = DateTime.UtcNow.AddDays(-1).ToString("O");
        var to = DateTime.UtcNow.AddDays(1).ToString("O");
        using var response = await admin.GetAsync($"api/v1/admin/revenue?from={from}&to={to}&bucket=day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var env = await ReadEnvelopeAsync(response);
        var data = env.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("totalRevenue").GetDecimal() >= 249_000m);
        Assert.True(data.GetProperty("grantCount").GetInt32() >= 1);
        Assert.NotEmpty(data.GetProperty("buckets").EnumerateArray());
        var references = data.GetProperty("references").EnumerateArray().Select(reference => reference.GetString()).ToList();
        Assert.Contains(reference, references);
        admin.Dispose();
    }

    // ---- PRIVACY BOUNDARY (§4.1 / R10) - the headline safety test ----------------------------------

    [SkippableFact]
    public async Task NoAdminEndpoint_ExposesAnotherUsersLedgerData()
    {
        Fixture.SkipIfNoDb();
        var (admin, _, _) = await CreateAdminClientAsync();
        var (_, targetUsername) = await CreateFreeClientAsync();
        var target = await GetUserAsync(targetUsername);

        // Give the target DISTINCTIVE ledger data (a member + a bank account). If ANY admin response
        // contained ledger data, one of these markers or ledger keys would appear in its JSON.
        const string ledgerMarker = "zzledgerleakmarker";
        const string bankMarker = "9999888877";
        await SeedLedgerForTargetAsync(target.Id, ledgerMarker, bankMarker);

        // Also record a grant so the detail view carries grant history (which is allowed - not ledger).
        using (var grant = await admin.PostAsJsonAsync($"api/v1/admin/users/{target.Uuid}/tier/grant", new { amount = 1000m }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var responses = new List<string>();
        foreach (var path in new[]
                 {
                     $"api/v1/admin/users?search={UsernamePrefix}",
                     $"api/v1/admin/users/{target.Uuid}",
                     "api/v1/admin/dashboard",
                     "api/v1/admin/revenue"
                 })
        {
            using var response = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            responses.Add(await response.Content.ReadAsStringAsync());
        }

        var body = string.Concat(responses);

        // No distinctive ledger VALUE leaked.
        Assert.DoesNotContain(ledgerMarker, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(bankMarker, body, StringComparison.Ordinal);

        // No ledger FIELD key leaked (these keys only exist on ledger DTOs, never on admin DTOs).
        foreach (var ledgerKey in new[] { "accountNumber", "accountHolderName", "bankBin", "payerMemberId", "expenseTime", "isOwnerRepresentative", "shares", "payerMember" })
            Assert.DoesNotContain(ledgerKey, body, StringComparison.OrdinalIgnoreCase);

        // Sanity: the admin detail DID return the account metadata + grant history it is supposed to.
        Assert.Contains(target.Uuid, body, StringComparison.Ordinal);

        admin.Dispose();
    }

    private async Task SeedLedgerForTargetAsync(ulong userId, string memberName, string bankAccountNumber)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Members.Add(new Member { UserId = userId, Name = memberName });
        context.BankAccounts.Add(new BankAccount
        {
            UserId = userId,
            BankBin = "970436",
            BankName = "Vietcombank",
            AccountNumber = bankAccountNumber,
            AccountHolderName = memberName,
            IsDefault = true
        });
        await context.SaveChangesAsync();
    }
}
