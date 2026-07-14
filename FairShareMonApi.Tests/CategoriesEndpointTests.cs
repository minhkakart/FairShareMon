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
/// End-to-end HTTP tests for the six guarded <c>api/v1/categories</c> endpoints via
/// WebApplicationFactory (real MariaDB/Redis - skippable). Covers the five-with-one-default seed on
/// register, create/update/delete, the atomic set-default swap, the default-not-deletable guard
/// (4002), the active-name duplicate (4001), reactivation over HTTP, soft-delete hide-from-selection
/// while preserving history, the resource-owned 404 (code 4000, never 403), validation
/// (<c>error.fields</c>, camelCase), and the auth guard. Assertions target stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class CategoriesEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    private const string Password = "password-8+";
    private const string Orange = "#F97316";
    private const string Blue = "#3B82F6";

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
        using var response = await client.GetAsync($"api/v1/categories?includeDeleted={includeDeleted.ToString().ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    private static async Task<JsonElement> CreateCategoryAsync(HttpClient client, string name, string color = Blue, string? icon = null)
    {
        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name, color, icon });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> DefaultCategoryAsync(HttpClient client) =>
        (await ListAsync(client)).Single(category => category.GetProperty("isDefault").GetBoolean());

    private static string Uuid(JsonElement category) => category.GetProperty("uuid").GetString()!;

    [SkippableFact]
    public async Task Register_NewAccount_CategoriesListHasFiveSeededWithAnUongDefault()
    {
        using var client = await CreateAuthorizedClientAsync();

        var categories = await ListAsync(client);

        Assert.Equal(5, categories.Length);
        var names = categories.Select(category => category.GetProperty("name").GetString()).ToArray();
        Assert.Contains("Ăn uống", names);
        Assert.Contains("Khác", names);
        var defaultCategory = Assert.Single(categories, category => category.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Ăn uống", defaultCategory.GetProperty("name").GetString());
        Assert.Equal("🍜", defaultCategory.GetProperty("icon").GetString());
        Assert.Equal(Orange, defaultCategory.GetProperty("color").GetString());
        // OQ11: default first, then A->Z.
        Assert.True(categories[0].GetProperty("isDefault").GetBoolean());
    }

    [SkippableFact]
    public async Task CreateCategory_AppearsInTheDefaultListWithColorAndIcon()
    {
        using var client = await CreateAuthorizedClientAsync();

        await CreateCategoryAsync(client, "Giải trí", Blue, "🎮");

        var created = (await ListAsync(client)).Single(category => category.GetProperty("name").GetString() == "Giải trí");
        Assert.Equal(Blue, created.GetProperty("color").GetString());
        Assert.Equal("🎮", created.GetProperty("icon").GetString());
        Assert.False(created.GetProperty("isDefault").GetBoolean()); // an API-created category is never default
    }

    [SkippableFact]
    public async Task UpdateCategory_PersistsNameColorIcon()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateCategoryAsync(client, "Giải trí", Blue, "🎮");

        using var response = await client.PutAsJsonAsync($"api/v1/categories/{Uuid(created)}",
            new { name = "Vui chơi", color = Orange, icon = "🎡" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("Vui chơi", data.GetProperty("name").GetString());
        Assert.Equal(Orange, data.GetProperty("color").GetString());
        Assert.Equal("🎡", data.GetProperty("icon").GetString());
    }

    [SkippableFact]
    public async Task CreateCategory_DuplicateActiveName_Returns400Code4001()
    {
        using var client = await CreateAuthorizedClientAsync();
        await CreateCategoryAsync(client, "Giải trí");

        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name = "Giải trí", color = Blue });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.CategoryNameDuplicate);
    }

    [SkippableFact]
    public async Task CreateCategory_DuplicateAgainstSeededNameCaseAndAccentInsensitive_Returns400Code4001()
    {
        using var client = await CreateAuthorizedClientAsync();

        // OQ5: "an uong" collides with the seeded "Ăn uống".
        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name = "an uong", color = Blue });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.CategoryNameDuplicate);
    }

    [SkippableFact]
    public async Task SetDefault_FlipsTheFlagAndClearsThePreviousDefault()
    {
        using var client = await CreateAuthorizedClientAsync();
        var oldDefaultUuid = Uuid(await DefaultCategoryAsync(client)); // "Ăn uống"
        var target = await CreateCategoryAsync(client, "Giải trí");

        using var response = await client.PutAsync($"api/v1/categories/{Uuid(target)}/default", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var categories = await ListAsync(client);
        var newDefault = Assert.Single(categories, category => category.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Giải trí", newDefault.GetProperty("name").GetString()); // target is now default
        Assert.False(categories.Single(category => Uuid(category) == oldDefaultUuid).GetProperty("isDefault").GetBoolean());
    }

    [SkippableFact]
    public async Task DeleteCategory_NonDefault_HiddenFromDefaultListButVisibleWithIncludeDeleted()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateCategoryAsync(client, "Giải trí");

        using var deleteResponse = await client.DeleteAsync($"api/v1/categories/{Uuid(created)}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var defaultList = await ListAsync(client, includeDeleted: false);
        Assert.DoesNotContain(defaultList, category => Uuid(category) == Uuid(created));

        var fullList = await ListAsync(client, includeDeleted: true);
        var deleted = fullList.Single(category => Uuid(category) == Uuid(created));
        Assert.True(deleted.GetProperty("isDeleted").GetBoolean()); // history preserved, flagged deleted
    }

    [SkippableFact]
    public async Task DeleteCategory_Default_Returns400Code4002AndItRemains()
    {
        using var client = await CreateAuthorizedClientAsync();
        var defaultUuid = Uuid(await DefaultCategoryAsync(client));

        using var response = await client.DeleteAsync($"api/v1/categories/{defaultUuid}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.DefaultCategoryNotDeletable);

        var stillDefault = await DefaultCategoryAsync(client);
        Assert.Equal(defaultUuid, Uuid(stillDefault)); // the default is untouched
    }

    [SkippableFact]
    public async Task CreateCategory_ReusingASoftDeletedName_ReactivatesTheSameRow()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateCategoryAsync(client, "Giải trí", Blue, "🎮");
        using (var deleteResponse = await client.DeleteAsync($"api/v1/categories/{Uuid(created)}"))
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Recreate with the same name but new color/icon -> revives the soft-deleted row (OQ4/OQ5).
        var recreated = await CreateCategoryAsync(client, "Giải trí", Orange, "🎡");

        Assert.Equal(Uuid(created), Uuid(recreated)); // same UUID
        Assert.False(recreated.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(Orange, recreated.GetProperty("color").GetString()); // color/icon overwritten
        Assert.Equal("🎡", recreated.GetProperty("icon").GetString());

        var actives = (await ListAsync(client)).Count(category => category.GetProperty("name").GetString() == "Giải trí");
        Assert.Equal(1, actives); // not duplicated
    }

    [SkippableFact]
    public async Task AnotherUsersCategory_Returns404Code4000_OnGetPutDeleteSetDefault_Never403()
    {
        using var ownerClient = await CreateAuthorizedClientAsync();
        using var strangerClient = await CreateAuthorizedClientAsync();
        var ownerCategoryUuid = Uuid(await CreateCategoryAsync(ownerClient, "Giải trí"));

        using var getResponse = await strangerClient.GetAsync($"api/v1/categories/{ownerCategoryUuid}");
        using var putResponse = await strangerClient.PutAsJsonAsync($"api/v1/categories/{ownerCategoryUuid}", new { name = "Hacked", color = Blue });
        using var deleteResponse = await strangerClient.DeleteAsync($"api/v1/categories/{ownerCategoryUuid}");
        using var setDefaultResponse = await strangerClient.PutAsync($"api/v1/categories/{ownerCategoryUuid}/default", null);

        foreach (var response in new[] { getResponse, putResponse, deleteResponse, setDefaultResponse })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, never 403 (no existence leak)
            using var envelope = await ReadEnvelopeAsync(response);
            AssertErrorEnvelope(envelope, ErrorCodes.CategoryNotFound);
        }
    }

    [SkippableFact]
    public async Task CreateCategory_InvalidColor_Returns400Code1001WithColorField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name = "Giải trí", color = "not-a-color" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        var fields = envelope.RootElement.GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("color", out var colorErrors)); // camelCase field key
        Assert.False(string.IsNullOrWhiteSpace(colorErrors[0].GetString())); // Vietnamese message present
    }

    [SkippableFact]
    public async Task CreateCategory_EmptyName_Returns400Code1001WithNameField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name = "", color = Blue });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("name", out _));
    }

    [SkippableFact]
    public async Task Categories_AnonymousRequest_Returns401WrappedEnvelope()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var listResponse = await client.GetAsync("api/v1/categories");
        using var createResponse = await client.PostAsJsonAsync("api/v1/categories", new { name = "Giải trí", color = Blue });

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        AssertErrorEnvelope(envelope, ErrorCodes.Unauthorized);
    }
}
