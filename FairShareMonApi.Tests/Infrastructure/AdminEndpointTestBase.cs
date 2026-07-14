using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Base for the M11 admin endpoint tests via <see cref="WebApplicationFactory{Program}"/> (real
/// MariaDB/Redis, skippable). Registers/logs-in FREE, PREMIUM and ADMIN callers (an admin is made by
/// flipping <c>users.role = ADMIN</c> directly then logging in, so the token carries ADMIN - mirroring
/// how the M10 base makes a Premium caller). The committed <c>Admin:</c> config stays empty, so the
/// startup seeder creates no admin on a normal boot; tests mint their own admins in the DB. Cleanup
/// sweeps <c>tier_grants</c> (no cascade FK, OQ5) by the prefix's user ids BEFORE the base deletes the
/// prefix's users, so the real DB is never left dirty.
/// </summary>
public abstract class AdminEndpointTestBase(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture)
{
    protected const string Password = "password-8+";

    protected static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    protected static void AssertErrorEnvelope(JsonDocument envelope, int expectedCode)
    {
        var root = envelope.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(expectedCode, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    protected async Task RegisterAsync(string username)
    {
        using var anonymous = Factory.CreateClient();
        using var register = await anonymous.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
    }

    protected async Task<(string AccessToken, string RefreshToken)> LoginTokensAsync(string username)
    {
        using var anonymous = Factory.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("api/v1/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var envelope = await ReadEnvelopeAsync(login);
        var data = envelope.RootElement.GetProperty("data");
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    protected async Task<HttpResponseMessage> RawLoginAsync(string username, string password = Password)
    {
        using var anonymous = Factory.CreateClient();
        return await anonymous.PostAsJsonAsync("api/v1/auth/login", new { username, password });
    }

    protected HttpClient ClientWithToken(string accessToken)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    protected async Task<HttpClient> LoginClientAsync(string username)
    {
        var (accessToken, _) = await LoginTokensAsync(username);
        return ClientWithToken(accessToken);
    }

    /// <summary>Registers a FREE user and returns a bearer client + the username.</summary>
    protected async Task<(HttpClient Client, string Username)> CreateFreeClientAsync()
    {
        var username = NewUsername();
        await RegisterAsync(username);
        return (await LoginClientAsync(username), username);
    }

    /// <summary>Registers a user, flips it to PREMIUM, logs in (token carries PREMIUM).</summary>
    protected async Task<(HttpClient Client, string Username)> CreatePremiumClientAsync()
    {
        var username = NewUsername();
        await RegisterAsync(username);
        await SetRoleTierStatusAsync(username, tier: UserTiers.Premium);
        return (await LoginClientAsync(username), username);
    }

    /// <summary>Registers a user, flips it to ADMIN, logs in (token carries ADMIN).</summary>
    protected async Task<(HttpClient Client, string Username, string Uuid)> CreateAdminClientAsync()
    {
        var username = NewUsername();
        await RegisterAsync(username);
        await SetRoleTierStatusAsync(username, role: UserRoles.Admin);
        var uuid = (await GetUserAsync(username)).Uuid;
        return (await LoginClientAsync(username), username, uuid);
    }

    protected async Task SetRoleTierStatusAsync(string username, string? role = null, string? tier = null, string? status = null)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users.FirstAsync(u => u.Username == username);
        if (role is not null) user.Role = role;
        if (tier is not null) user.Tier = tier;
        if (status is not null) user.Status = status;
        await context.SaveChangesAsync();
    }

    protected async Task<Database.Entities.User> GetUserAsync(string username)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AsNoTracking().SingleAsync(user => user.Username == username);
    }

    protected async Task<int> CountGrantsAsync(ulong userId, string action)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.TierGrants.CountAsync(grant => grant.UserId == userId && grant.Action == action);
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

            await context.TierGrants
                .Where(grant => userIds.Contains(grant.UserId) || userIds.Contains(grant.GrantedByUserId))
                .ExecuteDeleteAsync();

            // Children whose RESTRICT/no-cascade FKs would otherwise block the base user-cascade delete
            // (mirrors TierEndpointTestBase). Members cascade on user delete, so are left to the base.
            await context.Expenses.Where(expense => userIds.Contains(expense.UserId)).ExecuteDeleteAsync();
            await context.Events.Where(evt => userIds.Contains(evt.UserId)).ExecuteDeleteAsync();
            await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
            await context.AuditLogs.Where(log => userIds.Contains(log.ActorUserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
