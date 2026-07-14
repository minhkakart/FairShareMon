using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Shared base for the M5 expense/share/history HTTP endpoint tests via WebApplicationFactory (real
/// MariaDB/Redis, skippable). Provides authorized-client bootstrap plus helpers to read the
/// <c>ApiResult</c> envelope and to create the member/tag prerequisites and the expenses/shares the
/// endpoint tests drive. Cleanup is inherited from <see cref="AuthApiTestBase"/> (deletes the
/// prefix's users, which cascades to members/categories/tags/expenses/shares/expense_tags and - via
/// the audit actor FK - audit_logs).
/// </summary>
public abstract class ExpenseApiTestBase(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    protected const string Password = "password-8+";

    protected static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    protected static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    protected static void AssertErrorEnvelope(JsonDocument envelope, int expectedCode)
    {
        var root = envelope.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(expectedCode, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    protected async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var username = NewUsername();
        using var anonymous = Factory.CreateClient();
        using var register = await anonymous.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await anonymous.PostAsJsonAsync("api/v1/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var envelope = await ReadEnvelopeAsync(login);
        var accessToken = envelope.RootElement.GetProperty("data").GetProperty("accessToken").GetString();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    protected static string Uuid(JsonElement element) => element.GetProperty("uuid").GetString()!;

    protected static async Task<string> OwnerRepUuidAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/v1/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray()
            .Single(member => member.GetProperty("isOwnerRepresentative").GetBoolean())
            .GetProperty("uuid").GetString()!;
    }

    protected static async Task<JsonElement> DefaultCategoryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/v1/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray()
            .Single(category => category.GetProperty("isDefault").GetBoolean()).Clone();
    }

    protected static async Task<string> CreateMemberAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("api/v1/members", new { name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
    }

    protected static async Task<string> CreateTagAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("api/v1/tags", new { name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
    }

    protected static async Task DeleteMemberAsync(HttpClient client, string memberUuid)
    {
        using var response = await client.DeleteAsync($"api/v1/members/{memberUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    protected static async Task DeleteCategoryAsync(HttpClient client, string categoryUuid)
    {
        using var response = await client.DeleteAsync($"api/v1/categories/{categoryUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Creates an expense over HTTP and returns the full <c>ExpenseResponse</c> data element.</summary>
    protected static async Task<JsonElement> CreateExpenseAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("api/v1/expenses", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
    }

    protected static async Task<JsonElement> GetExpenseAsync(HttpClient client, string uuid)
    {
        using var response = await client.GetAsync($"api/v1/expenses/{uuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
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

            // Delete expenses FIRST: their RESTRICT FKs to categories/members would otherwise block the
            // base class's user-cascade delete. This cascades shares + expense_tags. Then sweep
            // audit_logs (the actor FK also cascades, but entity_uuid/expense_uuid carry no FK).
            await context.Expenses.Where(expense => userIds.Contains(expense.UserId)).ExecuteDeleteAsync();
            await context.AuditLogs.Where(log => userIds.Contains(log.ActorUserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
