using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FairShareMonApi.Constants;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// In-process endpoint tests via <see cref="WebApplicationFactory{Program}"/>: the anonymous
/// health endpoint returns a success envelope; the temporary [Authorize] probe proves the stub
/// auth pipeline 401s inside the ApiResult envelope (with and without a bogus Bearer token) and
/// stays hidden from swagger.json. No MariaDB/Redis required - nothing resolves them.
/// </summary>
public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static void AssertUnauthorizedEnvelope(HttpResponseMessage response, JsonDocument envelope)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var root = envelope.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(root.GetProperty("isSuccess").GetBoolean());

        var error = root.GetProperty("error");
        Assert.Equal(ErrorCodes.Unauthorized, error.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task GetHealth_Anonymous_Returns200SuccessEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("data").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task GetAuthProbe_WithoutToken_Returns401ErrorEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("api/v1/authprobe");

        using var envelope = await ReadEnvelopeAsync(response);
        AssertUnauthorizedEnvelope(response, envelope);
    }

    [Fact]
    public async Task GetAuthProbe_WithBogusBearerToken_Returns401ErrorEnvelope()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bogus-token-that-nobody-issued");

        using var response = await client.GetAsync("api/v1/authprobe");

        using var envelope = await ReadEnvelopeAsync(response);
        AssertUnauthorizedEnvelope(response, envelope);
    }

    [Fact]
    public async Task GetSwaggerJson_Generated_HidesAuthProbeButExposesHealth()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var swaggerJson = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/health", swaggerJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authprobe", swaggerJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSwaggerJson_Generated_ContainsBearerSecurityScheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("swagger/v1/swagger.json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bearer = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
    }
}
