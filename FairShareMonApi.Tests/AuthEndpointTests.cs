using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Sdk;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the five <c>api/v1/auth</c> endpoints via WebApplicationFactory
/// (real MariaDB/Redis - skippable when the DB is unreachable). Closes the Milestone-1 handoffs:
/// first e2e coverage of <c>ValidationException</c> -> 400 with camelCase <c>error.fields</c>, and
/// of <c>[ResponseWrapped]</c> auto-wrapping (refresh returns a plain DTO). Also owns the guarded
/// 401-envelope regression tests (moved here from <c>HealthEndpointTests</c>) and the per-operation
/// Swagger padlock contract. Assertions target stable error CODES, not message text.
/// </summary>
[Collection("AuthIntegration")]
public class AuthEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
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
        var error = root.GetProperty("error");
        Assert.Equal(expectedCode, error.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    private HttpClient CreateClient(string? bearerToken = null)
    {
        var client = Factory.CreateClient();
        if (bearerToken is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private async Task<string> RegisterAsync(HttpClient client, string? username = null)
    {
        var name = username ?? NewUsername();
        using var response = await client.PostAsJsonAsync("api/v1/auth/register", new { username = name, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return name;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(HttpClient client, string username, string password = Password)
    {
        using var response = await client.PostAsJsonAsync("api/v1/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    [SkippableFact]
    public async Task Register_InvalidPayload_Returns400WithCamelCaseFieldErrors()
    {
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/auth/register", new { username = "a!", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed); // first e2e ValidationException -> 400
        var fields = envelope.RootElement.GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("username", out var usernameErrors)); // camelCase field keys
        Assert.True(fields.TryGetProperty("password", out var passwordErrors));
        Assert.True(usernameErrors.GetArrayLength() >= 1);
        Assert.False(string.IsNullOrWhiteSpace(passwordErrors[0].GetString())); // Vietnamese message present
    }

    [SkippableFact]
    public async Task Register_ValidPayload_Returns200UserResponseWithoutPasswordData()
    {
        using var client = CreateClient();
        var username = NewUsername() + "UPPER";

        using var response = await client.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal(username.ToLowerInvariant(), data.GetProperty("username").GetString()); // stored lowercase
        Assert.Equal(UserTiers.Free, data.GetProperty("tier").GetString());
        Assert.Equal(36, data.GetProperty("uuid").GetString()!.Length);
        Assert.False(data.TryGetProperty("password", out _)); // no secret material in the response
        Assert.False(data.TryGetProperty("passwordHash", out _));
    }

    [SkippableFact]
    public async Task Register_DuplicateUsername_Returns400Code2000()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);

        using var response = await client.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.UsernameTaken);
    }

    [SkippableFact]
    public async Task Login_WrongPassword_Returns401Code2001()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);

        using var response = await client.PostAsJsonAsync("api/v1/auth/login", new { username, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.InvalidCredentials);
    }

    [SkippableFact]
    public async Task Login_ValidCredentials_ReturnsTokenPairEnvelope()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);

        using var response = await client.PostAsJsonAsync("api/v1/auth/login", new { username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(43, data.GetProperty("accessToken").GetString()!.Length); // opaque 32-byte Base64Url
        Assert.Equal(43, data.GetProperty("refreshToken").GetString()!.Length);
        Assert.True(data.GetProperty("accessTokenExpiresAt").GetDateTime() < data.GetProperty("refreshTokenExpiresAt").GetDateTime());
    }

    [SkippableFact]
    public async Task Refresh_ValidToken_ReturnsAutoWrappedEnvelopeAndRevokesOldPair()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);
        var oldPair = await LoginAsync(client, username);

        using var response = await client.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = oldPair.RefreshToken });

        // The action returns a PLAIN TokenPairResponse - [ResponseWrapped] must wrap it (handoff).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var newAccessToken = root.GetProperty("data").GetProperty("accessToken").GetString()!;
        Assert.Equal(43, newAccessToken.Length);
        Assert.NotEqual(oldPair.AccessToken, newAccessToken);

        // Full pair rotation: the old access token no longer authenticates.
        using var oldTokenClient = CreateClient(oldPair.AccessToken);
        using var guardedResponse = await oldTokenClient.PostAsync("api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, guardedResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Refresh_InvalidToken_Returns401Code2002()
    {
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = "never-issued-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.InvalidRefreshToken);
    }

    [SkippableFact]
    public async Task FullFlow_RegisterLoginLogout_TokenIsUnusableAfterLogout()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);
        var pair = await LoginAsync(client, username);

        using var authorizedClient = CreateClient(pair.AccessToken);
        using var logoutResponse = await authorizedClient.PostAsync("api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        using var logoutEnvelope = await ReadEnvelopeAsync(logoutResponse);
        Assert.True(logoutEnvelope.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(
            logoutEnvelope.RootElement.GetProperty("data").GetProperty("message").GetString()));

        // The same access token afterwards -> 401 wrapped (replaces the old AuthProbe regression).
        using var replayResponse = await authorizedClient.PostAsync("api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        using var replayEnvelope = await ReadEnvelopeAsync(replayResponse);
        AssertErrorEnvelope(replayEnvelope, ErrorCodes.Unauthorized);

        // Logout revokes the whole pair - the paired refresh token is dead too.
        using var refreshResponse = await client.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [SkippableFact]
    public async Task ChangePassword_TwoDevices_BothTokensRevokedAndNewPasswordWorks()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);
        var deviceOne = await LoginAsync(client, username);
        var deviceTwo = await LoginAsync(client, username);

        using var deviceOneClient = CreateClient(deviceOne.AccessToken);
        using var changeResponse = await deviceOneClient.PostAsJsonAsync(
            "api/v1/auth/change-password", new { currentPassword = Password, newPassword = "brand-new-pw-9" });

        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);
        using var changeEnvelope = await ReadEnvelopeAsync(changeResponse);
        Assert.True(changeEnvelope.RootElement.GetProperty("isSuccess").GetBoolean());

        // Every logged-in device is forced to re-login (spec §3.1).
        using var deviceOneRetry = await deviceOneClient.PostAsync("api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, deviceOneRetry.StatusCode);
        using var deviceTwoClient = CreateClient(deviceTwo.AccessToken);
        using var deviceTwoRetry = await deviceTwoClient.PostAsync("api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, deviceTwoRetry.StatusCode);

        var newPair = await LoginAsync(client, username, "brand-new-pw-9");
        Assert.Equal(43, newPair.AccessToken.Length);
    }

    [SkippableFact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400Code2003()
    {
        using var client = CreateClient();
        var username = await RegisterAsync(client);
        var pair = await LoginAsync(client, username);

        using var authorizedClient = CreateClient(pair.AccessToken);
        using var response = await authorizedClient.PostAsJsonAsync(
            "api/v1/auth/change-password", new { currentPassword = "wrong-current", newPassword = "brand-new-pw-9" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.CurrentPasswordIncorrect);
    }

    [SkippableFact] // needs no DB itself, but the shared class setup skips uniformly when the DB is down
    public async Task Logout_WithoutToken_Returns401ErrorEnvelope()
    {
        // Moved from HealthEndpointTests: the guarded-endpoint 401 contract belongs to auth. The
        // challenge fires before any whitelist lookup.
        using var client = CreateClient();

        using var response = await client.PostAsync("api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.Unauthorized);
    }

    [SkippableFact]
    public async Task Logout_WithBogusBearerToken_Returns401ErrorEnvelope()
    {
        // Moved from HealthEndpointTests. Exercises the real whitelist lookup (Redis-or-DB).
        using var client = CreateClient("bogus-token-that-nobody-issued");

        using var response = await client.PostAsync("api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.Unauthorized);
    }

    [SkippableFact] // needs no DB itself, but the shared class setup skips uniformly when the DB is down
    public async Task SwaggerJson_PadlockOnlyOnGuardedOperations()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(HasBearerRequirement(paths, "/api/v1/auth/logout", "post"));
        Assert.True(HasBearerRequirement(paths, "/api/v1/auth/change-password", "post"));
        Assert.False(HasBearerRequirement(paths, "/api/v1/auth/register", "post"));
        Assert.False(HasBearerRequirement(paths, "/api/v1/auth/login", "post"));
        Assert.False(HasBearerRequirement(paths, "/api/v1/auth/refresh", "post"));
        Assert.False(HasBearerRequirement(paths, "/api/v1/health", "get"));
    }

    private static bool HasBearerRequirement(JsonElement paths, string path, string verb)
    {
        foreach (var pathProperty in paths.EnumerateObject())
        {
            if (!string.Equals(pathProperty.Name, path, StringComparison.OrdinalIgnoreCase))
                continue;

            var operation = pathProperty.Value.GetProperty(verb);
            return operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;
        }

        throw new XunitException($"Path '{path}' not found in the swagger document.");
    }
}
