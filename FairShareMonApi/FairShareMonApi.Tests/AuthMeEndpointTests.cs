using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for <c>GET api/v1/auth/me</c> (the current-user profile endpoint, OQ2a/OQ4a)
/// via <see cref="WebApplicationFactory{Program}"/> against the real MariaDB/Redis (skippable when the
/// DB is unreachable). Proves: the guarded endpoint returns the caller's OWN profile including the new
/// <c>role</c> field for both a USER and an ADMIN caller; anonymous -&gt; the wired 401 <c>1002</c>
/// envelope; a revoked token -&gt; 401 <c>1002</c>; the profile is a LIVE DB read (a DB-side tier/role
/// change is reflected on the SAME unrefreshed token - OQ3a); the register response now additively
/// carries <c>role: USER</c>; and no secret material is ever serialized. Reuses the M11 admin harness
/// for its register/login/role-flip helpers. Assertions target stable error CODES, never message text.
/// </summary>
[Collection("AuthIntegration")]
public class AuthMeEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AdminEndpointTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    [SkippableFact]
    public async Task GetMe_ValidTokenNormalUser_Returns200OwnProfileWithRoleUser()
    {
        Fixture.SkipIfNoDb();
        var (client, username) = await CreateFreeClientAsync();
        var seeded = await GetUserAsync(username);

        using var response = await client.GetAsync("api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);

        var data = root.GetProperty("data");
        Assert.Equal(seeded.Uuid, data.GetProperty("uuid").GetString());
        Assert.Equal(username, data.GetProperty("username").GetString());
        Assert.Equal(UserTiers.Free, data.GetProperty("tier").GetString());
        Assert.Equal(UserRoles.User, data.GetProperty("role").GetString());
        Assert.True(data.GetProperty("createdAt").GetDateTime() > new DateTime(2000, 1, 1)); // live createdAt present

        // Documented DTO invariant: no secret material.
        Assert.False(data.TryGetProperty("password", out _));
        Assert.False(data.TryGetProperty("passwordHash", out _));

        client.Dispose();
    }

    [SkippableFact]
    public async Task GetMe_ValidTokenAdminUser_ReturnsOwnProfileWithRoleAdmin()
    {
        Fixture.SkipIfNoDb();
        var (admin, username, uuid) = await CreateAdminClientAsync();

        using var response = await admin.GetAsync("api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(uuid, data.GetProperty("uuid").GetString());
        Assert.Equal(username, data.GetProperty("username").GetString());
        Assert.Equal(UserRoles.Admin, data.GetProperty("role").GetString()); // the right role comes back for an admin

        admin.Dispose();
    }

    [SkippableFact]
    public async Task GetMe_Anonymous_Returns401Code1002()
    {
        Fixture.SkipIfNoDb();
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync("api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized); // guarded: FallbackPolicy challenge
    }

    [SkippableFact]
    public async Task GetMe_RevokedToken_Returns401Code1002()
    {
        Fixture.SkipIfNoDb();
        var username = NewUsername();
        await RegisterAsync(username);
        var (accessToken, _) = await LoginTokensAsync(username);
        using var client = ClientWithToken(accessToken);

        // The token works, then logout revokes it (reusing the suite's revocation path).
        using (var before = await client.GetAsync("api/v1/auth/me"))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using (var logout = await client.PostAsync("api/v1/auth/logout", content: null))
            Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        using var response = await client.GetAsync("api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    [SkippableFact]
    public async Task GetMe_AfterDbTierAndRoleChange_ReflectsLiveValuesOnSameToken()
    {
        Fixture.SkipIfNoDb();
        // Token minted while the user is FREE/USER; /auth/me is a LIVE DB read (OQ3a), so a DB-side
        // change is reflected on the SAME unrefreshed, un-busted token - no re-login, no cache-bust.
        var (client, username) = await CreateFreeClientAsync();

        using (var beforeChange = await client.GetAsync("api/v1/auth/me"))
        {
            var data = (await ReadEnvelopeAsync(beforeChange)).RootElement.GetProperty("data");
            Assert.Equal(UserTiers.Free, data.GetProperty("tier").GetString());
            Assert.Equal(UserRoles.User, data.GetProperty("role").GetString());
        }

        await SetRoleTierStatusAsync(username, role: UserRoles.Admin, tier: UserTiers.Premium);

        using var response = await client.GetAsync("api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var live = (await ReadEnvelopeAsync(response)).RootElement.GetProperty("data");
        Assert.Equal(UserTiers.Premium, live.GetProperty("tier").GetString()); // live tier, not the token's cached FREE
        Assert.Equal(UserRoles.Admin, live.GetProperty("role").GetString());    // live role, not the token's cached USER

        client.Dispose();
    }

    [SkippableFact]
    public async Task Register_Response_AdditivelyCarriesRoleUser()
    {
        Fixture.SkipIfNoDb();
        // Additive-field regression guard: UserResponse now also rides the register response (OQ1a
        // trade-off). Lock that role is present and defaults to USER for a fresh account.
        var username = NewUsername();
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await ReadEnvelopeAsync(response)).RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("role", out var role));
        Assert.Equal(UserRoles.User, role.GetString());
        Assert.Equal(UserTiers.Free, data.GetProperty("tier").GetString());
    }
}
