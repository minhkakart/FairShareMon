using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// In-process endpoint tests via <see cref="WebApplicationFactory{Program}"/> for the anonymous
/// health endpoint and the general Swagger document contract. The guarded-endpoint 401-envelope
/// tests moved to <c>AuthEndpointTests</c> together with the rest of the auth endpoint coverage.
/// </summary>
public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

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
    public async Task GetSwaggerJson_Generated_HidesAuthProbeButExposesHealth()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var swaggerJson = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/health", swaggerJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/v1/auth/login", swaggerJson, StringComparison.OrdinalIgnoreCase);
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
