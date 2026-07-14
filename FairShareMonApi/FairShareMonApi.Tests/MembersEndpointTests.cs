using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the five guarded <c>api/v1/members</c> endpoints via
/// WebApplicationFactory (real MariaDB/Redis - skippable when the DB is unreachable). Covers the
/// resource-owned 404 (code 3000, never 403), the owner-rep-on-register invariant, soft-delete
/// hide-from-selection while preserving history, the owner-rep delete refusal (3001), rename
/// (incl. owner-rep), validation (1001), free-form duplicates, and the auth guard. Assertions
/// target stable error CODES, not message text.
/// </summary>
[Collection("AuthIntegration")]
public class MembersEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    private const string Password = "password-8+";

    private static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static void AssertErrorEnvelope(JsonDocument envelope, int expectedCode)
    {
        var root = envelope.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(expectedCode, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    /// <summary>Registers a fresh user and returns an authorized client (Bearer access token set).</summary>
    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var username = NewUsername();
        using (var anonymous = Factory.CreateClient())
        {
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
    }

    private static async Task<JsonElement[]> ListAsync(HttpClient client, bool includeDeleted = false)
    {
        using var response = await client.GetAsync($"api/v1/members?includeDeleted={includeDeleted.ToString().ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        // Clone: the elements must outlive the JsonDocument, which is disposed on return.
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    private static async Task<string> CreateMemberAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("api/v1/members", new { name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
    }

    private static async Task<string> OwnerRepUuidAsync(HttpClient client)
    {
        var members = await ListAsync(client);
        return members.Single(member => member.GetProperty("isOwnerRepresentative").GetBoolean()).GetProperty("uuid").GetString()!;
    }

    [SkippableFact]
    public async Task Register_NewAccount_MembersListHasExactlyOneOwnerRepNamedToi()
    {
        using var client = await CreateAuthorizedClientAsync();

        var members = await ListAsync(client);

        var member = Assert.Single(members);
        Assert.True(member.GetProperty("isOwnerRepresentative").GetBoolean());
        Assert.Equal(Member.OwnerRepresentativeDefaultName, member.GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task CreateMember_AppearsInTheDefaultList()
    {
        using var client = await CreateAuthorizedClientAsync();

        await CreateMemberAsync(client, "An");

        var names = (await ListAsync(client)).Select(member => member.GetProperty("name").GetString()).ToArray();
        Assert.Contains("An", names);
        Assert.Contains(Member.OwnerRepresentativeDefaultName, names); // owner-rep still present
    }

    [SkippableFact]
    public async Task DeleteMember_HiddenFromDefaultListButVisibleWithIncludeDeleted()
    {
        using var client = await CreateAuthorizedClientAsync();
        var memberUuid = await CreateMemberAsync(client, "An");

        using var deleteResponse = await client.DeleteAsync($"api/v1/members/{memberUuid}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var defaultList = await ListAsync(client, includeDeleted: false);
        Assert.DoesNotContain(defaultList, member => member.GetProperty("uuid").GetString() == memberUuid);

        var fullList = await ListAsync(client, includeDeleted: true);
        var deleted = fullList.Single(member => member.GetProperty("uuid").GetString() == memberUuid);
        Assert.True(deleted.GetProperty("isDeleted").GetBoolean()); // history preserved, flagged deleted
    }

    [SkippableFact]
    public async Task DeleteOwnerRepresentative_Returns400Code3001AndItRemains()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRepUuid = await OwnerRepUuidAsync(client);

        using var response = await client.DeleteAsync($"api/v1/members/{ownerRepUuid}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.OwnerRepresentativeNotDeletable);

        var stillThere = await ListAsync(client);
        Assert.Contains(stillThere, member => member.GetProperty("uuid").GetString() == ownerRepUuid);
    }

    [SkippableFact]
    public async Task RenameMember_PersistsNewName()
    {
        using var client = await CreateAuthorizedClientAsync();
        var memberUuid = await CreateMemberAsync(client, "An");

        using var response = await client.PutAsJsonAsync($"api/v1/members/{memberUuid}", new { name = "Bình" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.Equal("Bình", envelope.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task RenameOwnerRepresentative_IsAllowed()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRepUuid = await OwnerRepUuidAsync(client);

        using var response = await client.PutAsJsonAsync($"api/v1/members/{ownerRepUuid}", new { name = "Chủ sổ thật" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("Chủ sổ thật", data.GetProperty("name").GetString());
        Assert.True(data.GetProperty("isOwnerRepresentative").GetBoolean()); // still the owner-rep
    }

    [SkippableFact]
    public async Task CreateMember_EmptyName_Returns400Code1001WithNameField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/members", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        var fields = envelope.RootElement.GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("name", out var nameErrors)); // camelCase field key
        Assert.False(string.IsNullOrWhiteSpace(nameErrors[0].GetString())); // Vietnamese message present
    }

    [SkippableFact]
    public async Task CreateMember_DuplicateNames_AreAllowed()
    {
        using var client = await CreateAuthorizedClientAsync();

        await CreateMemberAsync(client, "An");
        await CreateMemberAsync(client, "An"); // OQ6: free-form, duplicates allowed

        var anCount = (await ListAsync(client)).Count(member => member.GetProperty("name").GetString() == "An");
        Assert.Equal(2, anCount);
    }

    [SkippableFact]
    public async Task AnotherUsersMember_Returns404Code3000_OnGetPutDelete_Never403()
    {
        using var ownerClient = await CreateAuthorizedClientAsync();
        using var strangerClient = await CreateAuthorizedClientAsync();
        var ownerMemberUuid = await CreateMemberAsync(ownerClient, "An");

        using var getResponse = await strangerClient.GetAsync($"api/v1/members/{ownerMemberUuid}");
        using var putResponse = await strangerClient.PutAsJsonAsync($"api/v1/members/{ownerMemberUuid}", new { name = "Hacked" });
        using var deleteResponse = await strangerClient.DeleteAsync($"api/v1/members/{ownerMemberUuid}");

        foreach (var response in new[] { getResponse, putResponse, deleteResponse })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, never 403 (no existence leak)
            using var envelope = await ReadEnvelopeAsync(response);
            AssertErrorEnvelope(envelope, ErrorCodes.MemberNotFound);
        }
    }

    [SkippableFact]
    public async Task Members_AnonymousRequest_Returns401WrappedEnvelope()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var listResponse = await client.GetAsync("api/v1/members");
        using var createResponse = await client.PostAsJsonAsync("api/v1/members", new { name = "An" });

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        AssertErrorEnvelope(envelope, ErrorCodes.Unauthorized);
    }
}
