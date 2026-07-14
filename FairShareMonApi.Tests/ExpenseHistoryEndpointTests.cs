using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for <c>GET api/v1/expenses/{uuid}/history</c> (§3.8) via
/// WebApplicationFactory (real MariaDB/Redis - skippable). Covers Create/Update/Delete entries with
/// before/after snapshots, the empty list for a foreign or unknown uuid (leaks nothing, OQ17), and
/// that a hard-deleted-but-owned expense still returns its create + delete history (§3.8). Assertions
/// target the audit entry shape.
/// </summary>
[Collection("AuthIntegration")]
public class ExpenseHistoryEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static async Task<JsonElement[]> HistoryAsync(HttpClient client, string uuid)
    {
        using var response = await client.GetAsync($"api/v1/expenses/{uuid}/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    [SkippableFact]
    public async Task History_AfterCreate_HasExpenseCreateEntryWithNullBeforeAndNonNullAfter()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        var history = await HistoryAsync(client, Uuid(created));

        var createEntry = history.First(entry => entry.GetProperty("entityType").GetString() == "Expense"
            && entry.GetProperty("action").GetString() == "Create");
        Assert.Equal(JsonValueKind.Null, createEntry.GetProperty("before").ValueKind);
        Assert.Equal(JsonValueKind.Object, createEntry.GetProperty("after").ValueKind);
        Assert.Equal("Ăn trưa", createEntry.GetProperty("after").GetProperty("name").GetString()); // denormalized snapshot
    }

    [SkippableFact]
    public async Task History_AfterUpdate_HasUpdateEntryWithBeforeAndAfter()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        using (var update = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(created)}", new { name = "Ăn tối", expenseTime = Noon }))
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var history = await HistoryAsync(client, Uuid(created));

        var updateEntry = history.Single(entry => entry.GetProperty("action").GetString() == "Update");
        Assert.Equal("Ăn trưa", updateEntry.GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Ăn tối", updateEntry.GetProperty("after").GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task History_AfterDelete_StillReturnsCreateAndDeleteEntries()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });
        var uuid = Uuid(created);

        using (var delete = await client.DeleteAsync($"api/v1/expenses/{uuid}"))
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        // The expense is gone (404), but its history survives (§3.8).
        using (var getResponse = await client.GetAsync($"api/v1/expenses/{uuid}"))
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var history = await HistoryAsync(client, uuid);
        Assert.Contains(history, entry => entry.GetProperty("action").GetString() == "Create");
        Assert.Contains(history, entry => entry.GetProperty("action").GetString() == "Delete");
    }

    [SkippableFact]
    public async Task History_AnotherUsersExpense_ReturnsEmptyList()
    {
        using var ownerClient = await CreateAuthorizedClientAsync();
        using var strangerClient = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(ownerClient, new { name = "Ăn trưa", expenseTime = Noon });

        var history = await HistoryAsync(strangerClient, Uuid(created));

        Assert.Empty(history); // resource-owned: a foreign uuid leaks nothing (OQ17)
    }

    [SkippableFact]
    public async Task History_UnknownExpenseUuid_ReturnsEmptyList()
    {
        using var client = await CreateAuthorizedClientAsync();

        var history = await HistoryAsync(client, "no-such-expense");

        Assert.Empty(history);
    }
}
