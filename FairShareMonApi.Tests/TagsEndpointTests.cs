using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the five guarded <c>api/v1/tags</c> endpoints via WebApplicationFactory
/// (real MariaDB/Redis - skippable). Covers create/rename/delete, reactivation-on-name-reuse (same
/// UUID revived, not a duplicate), the active-name duplicate (5001), soft-delete
/// hide-from-selection while preserving history, the resource-owned 404 (code 5000, never 403),
/// validation (<c>error.fields</c>, camelCase), and the auth guard. Assertions target stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class TagsEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
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

    private async Task<HttpClient> CreateAuthorizedClientAsync()
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

    private static async Task<JsonElement[]> ListAsync(HttpClient client, bool includeDeleted = false)
    {
        using var response = await client.GetAsync($"api/v1/tags?includeDeleted={includeDeleted.ToString().ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    private static async Task<JsonElement> CreateTagAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("api/v1/tags", new { name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
    }

    private static string Uuid(JsonElement tag) => tag.GetProperty("uuid").GetString()!;

    [SkippableFact]
    public async Task Register_NewAccount_HasNoTags()
    {
        using var client = await CreateAuthorizedClientAsync();

        Assert.Empty(await ListAsync(client)); // tags are not seeded
    }

    [SkippableFact]
    public async Task CreateTag_AppearsInTheDefaultList()
    {
        using var client = await CreateAuthorizedClientAsync();

        await CreateTagAsync(client, "Công tác");

        var names = (await ListAsync(client)).Select(tag => tag.GetProperty("name").GetString()).ToArray();
        Assert.Contains("Công tác", names);
    }

    [SkippableFact]
    public async Task CreateTag_DuplicateActiveName_Returns400Code5001()
    {
        using var client = await CreateAuthorizedClientAsync();
        await CreateTagAsync(client, "Công tác");

        using var response = await client.PostAsJsonAsync("api/v1/tags", new { name = "Công tác" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.TagNameDuplicate);
    }

    [SkippableFact]
    public async Task RenameTag_PersistsNewName()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateTagAsync(client, "Công tác");

        using var response = await client.PutAsJsonAsync($"api/v1/tags/{Uuid(created)}", new { name = "Đi công tác" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.Equal("Đi công tác", envelope.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task DeleteTag_HiddenFromDefaultListButVisibleWithIncludeDeleted()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateTagAsync(client, "Công tác");

        using var deleteResponse = await client.DeleteAsync($"api/v1/tags/{Uuid(created)}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var defaultList = await ListAsync(client, includeDeleted: false);
        Assert.DoesNotContain(defaultList, tag => Uuid(tag) == Uuid(created));

        var fullList = await ListAsync(client, includeDeleted: true);
        var deleted = fullList.Single(tag => Uuid(tag) == Uuid(created));
        Assert.True(deleted.GetProperty("isDeleted").GetBoolean()); // history preserved
    }

    [SkippableFact]
    public async Task CreateTag_ReusingASoftDeletedName_ReactivatesTheSameRow()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateTagAsync(client, "Công tác");
        using (var deleteResponse = await client.DeleteAsync($"api/v1/tags/{Uuid(created)}"))
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var recreated = await CreateTagAsync(client, "Công tác"); // reuse the deleted name

        Assert.Equal(Uuid(created), Uuid(recreated)); // same UUID revived, not a duplicate
        Assert.False(recreated.GetProperty("isDeleted").GetBoolean());

        var actives = (await ListAsync(client)).Count(tag => tag.GetProperty("name").GetString() == "Công tác");
        Assert.Equal(1, actives);
    }

    [SkippableFact]
    public async Task AnotherUsersTag_Returns404Code5000_OnGetPutDelete_Never403()
    {
        using var ownerClient = await CreateAuthorizedClientAsync();
        using var strangerClient = await CreateAuthorizedClientAsync();
        var ownerTagUuid = Uuid(await CreateTagAsync(ownerClient, "Công tác"));

        using var getResponse = await strangerClient.GetAsync($"api/v1/tags/{ownerTagUuid}");
        using var putResponse = await strangerClient.PutAsJsonAsync($"api/v1/tags/{ownerTagUuid}", new { name = "Hacked" });
        using var deleteResponse = await strangerClient.DeleteAsync($"api/v1/tags/{ownerTagUuid}");

        foreach (var response in new[] { getResponse, putResponse, deleteResponse })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, never 403 (no existence leak)
            using var envelope = await ReadEnvelopeAsync(response);
            AssertErrorEnvelope(envelope, ErrorCodes.TagNotFound);
        }
    }

    [SkippableFact]
    public async Task CreateTag_EmptyName_Returns400Code1001WithNameField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/tags", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        var fields = envelope.RootElement.GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("name", out var nameErrors)); // camelCase field key
        Assert.False(string.IsNullOrWhiteSpace(nameErrors[0].GetString())); // Vietnamese message present
    }

    [SkippableFact]
    public async Task Tags_AnonymousRequest_Returns401WrappedEnvelope()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var listResponse = await client.GetAsync("api/v1/tags");
        using var createResponse = await client.PostAsJsonAsync("api/v1/tags", new { name = "Công tác" });

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        AssertErrorEnvelope(envelope, ErrorCodes.Unauthorized);
    }
}
