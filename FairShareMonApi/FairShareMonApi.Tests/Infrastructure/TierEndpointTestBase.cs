using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Standalone base for the M10 tier endpoint tests. Unlike <see cref="ExpenseApiTestBase"/> it does NOT
/// bind to the default <c>WebApplicationFactory&lt;Program&gt;</c> class fixture, so each concrete test
/// class supplies its OWN low-limit factory (config override) via its own <c>IClassFixture&lt;T&gt;</c>
/// and passes it up as the base-typed factory. Provides register/login (FREE and PREMIUM), a direct
/// <c>users.tier</c> flip (no upgrade endpoint exists), direct DB seeding to push a Free user OVER a
/// limit (so §4.9 read/edit/delete can be proved), and prefix-scoped cleanup that leaves the real DB
/// clean. Tests skip when MariaDB is unreachable.
/// </summary>
public abstract class TierEndpointTestBase(WebApplicationFactory<Program> factory, DatabaseFixture fixture) : IAsyncLifetime
{
    protected const string Password = "password-8+";

    private int _usernameCounter;

    protected WebApplicationFactory<Program> Factory { get; } = factory;

    protected DatabaseFixture Fixture { get; } = fixture;

    protected string UsernamePrefix { get; } = "t" + Guid.NewGuid().ToString("N")[..10] + "_";

    public virtual Task InitializeAsync()
    {
        Fixture.SkipIfNoDb();
        return Task.CompletedTask;
    }

    protected string NewUsername() => UsernamePrefix + Interlocked.Increment(ref _usernameCounter);

    protected static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    protected static void AssertErrorEnvelope(JsonDocument envelope, int expectedCode)
    {
        var root = envelope.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(expectedCode, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    /// <summary>Registers + logs in a FREE user; returns the client and the username (for re-login/flip).</summary>
    protected async Task<(HttpClient Client, string Username)> CreateFreeClientAsync()
    {
        var username = NewUsername();
        await RegisterAsync(username);
        var client = await LoginClientAsync(username);
        return (client, username);
    }

    /// <summary>Registers a user, flips it to PREMIUM in the DB, then logs in so the new token carries PREMIUM.</summary>
    protected async Task<HttpClient> CreatePremiumClientAsync()
    {
        var username = NewUsername();
        await RegisterAsync(username);
        await SetUserTierAsync(username, UserTiers.Premium);
        return await LoginClientAsync(username);
    }

    private async Task RegisterAsync(string username)
    {
        using var anonymous = Factory.CreateClient();
        using var register = await anonymous.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
    }

    /// <summary>Logs in the user and returns a bearer-authorized client (the token reflects the CURRENT db tier).</summary>
    protected async Task<HttpClient> LoginClientAsync(string username)
    {
        using var anonymous = Factory.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("api/v1/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var envelope = await ReadEnvelopeAsync(login);
        var accessToken = envelope.RootElement.GetProperty("data").GetProperty("accessToken").GetString();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    protected async Task SetUserTierAsync(string username, string tier)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Users
            .Where(user => user.Username == username)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Tier, tier));
    }

    protected async Task<User> GetUserAsync(string username)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AsNoTracking().SingleAsync(user => user.Username == username);
    }

    // ---- Direct DB seeding (bypasses the create-guard, to push a Free user OVER a limit) -----------

    protected async Task<Member> GetOwnerRepAsync(ulong userId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Members.AsNoTracking()
            .SingleAsync(member => member.UserId == userId && member.IsOwnerRepresentative);
    }

    protected async Task<Category> GetDefaultCategoryAsync(ulong userId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Categories.AsNoTracking()
            .SingleAsync(category => category.UserId == userId && category.IsDefault);
    }

    protected async Task<Member> SeedMemberAsync(ulong userId, string name)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = new Member { UserId = userId, Name = name };
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    protected async Task<Event> SeedOpenEventAsync(ulong userId, string name)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = new Event
        {
            UserId = userId,
            Name = name,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            IsClosed = false
        };
        context.Events.Add(evt);
        await context.SaveChangesAsync();
        return evt;
    }

    protected async Task<Expense> SeedExpenseAsync(ulong userId, ulong payerMemberId, ulong categoryId, DateTime expenseTime)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expense = new Expense
        {
            UserId = userId,
            Name = "Chi tiêu",
            ExpenseTime = expenseTime,
            PayerMemberId = payerMemberId,
            CategoryId = categoryId
        };
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense;
    }

    protected async Task<BankAccount> SeedBankAccountAsync(ulong userId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new BankAccount
        {
            UserId = userId,
            BankBin = "970436",
            BankName = "Vietcombank",
            AccountNumber = "0123456789",
            AccountHolderName = "Nguyen Van A",
            IsDefault = true
        };
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }

    public virtual async Task DisposeAsync()
    {
        if (!Fixture.IsAvailable)
            return;

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userIds = await context.Users
            .Where(user => user.Username.StartsWith(UsernamePrefix))
            .Select(user => user.Id)
            .ToListAsync();

        // Delete children whose RESTRICT/no-cascade FKs would otherwise block the user cascade.
        await context.Expenses.Where(expense => userIds.Contains(expense.UserId)).ExecuteDeleteAsync();
        await context.Events.Where(evt => userIds.Contains(evt.UserId)).ExecuteDeleteAsync();
        await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
        await context.AuditLogs.Where(log => userIds.Contains(log.ActorUserId)).ExecuteDeleteAsync();
        await context.Users.Where(user => user.Username.StartsWith(UsernamePrefix)).ExecuteDeleteAsync();
    }
}
