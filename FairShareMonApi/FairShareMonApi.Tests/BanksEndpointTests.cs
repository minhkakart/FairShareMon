using System.Net;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for <c>GET /api/v1/banks</c> via WebApplicationFactory with a STUBBED outbound
/// VietQR directory (deterministic, never hits the real vietqr.vn) and the real MariaDB/Redis for auth
/// (skippable). Proves the endpoint is authenticated-only but NOT Premium-gated (OQ-A(a)): a Free-tier
/// authenticated user gets 200; an anonymous request gets 401 (not 403). Also proves the response envelope
/// shape — <c>isSuccess:true</c>, <c>data</c> a non-empty array of <c>{bin,code,name,shortName,logoUrl}</c>
/// in camelCase with a fully-built logoUrl and NO imageId field — and that a non-6-digit BIN is dropped.
/// </summary>
[Collection("AuthIntegration")]
public class BanksEndpointTests(BanksStubWebApplicationFactory factory, DatabaseFixture fixture)
    : TierEndpointTestBase(factory, fixture), IClassFixture<BanksStubWebApplicationFactory>, IClassFixture<DatabaseFixture>
{
    private const string Route = "api/v1/banks";

    private static readonly HashSet<string> ExpectedFields = ["bin", "code", "name", "shortName", "logoUrl"];

    [SkippableFact]
    public async Task ListBanks_FreeAuthenticatedUser_Returns200WithCamelCaseDirectoryAndNoImageId()
    {
        var (client, _) = await CreateFreeClientAsync(); // Free tier proves there is no Premium gate

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // 200, not 403
        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());

        var banks = root.GetProperty("data").EnumerateArray().ToList();
        Assert.NotEmpty(banks);
        Assert.Equal(2, banks.Count); // the 5-digit-BIN entry was dropped during normalization

        foreach (var bank in banks)
        {
            var fieldNames = bank.EnumerateObject().Select(property => property.Name).ToHashSet();
            Assert.Equal(ExpectedFields, fieldNames);           // exactly the contract fields, camelCase
            Assert.DoesNotContain("imageId", fieldNames);       // imageId never leaves the backend
            Assert.False(string.IsNullOrWhiteSpace(bank.GetProperty("bin").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(bank.GetProperty("logoUrl").GetString()));
        }

        // The VCB entry's logoUrl is fully built from BaseUrl + ImagePath + imageId.
        var vcb = banks.Single(bank => bank.GetProperty("bin").GetString() == "970436");
        Assert.Equal("Vietcombank", vcb.GetProperty("shortName").GetString());
        Assert.Equal("https://vietqr.vn/api/vietqr/images/img-vcb", vcb.GetProperty("logoUrl").GetString());
    }

    [SkippableFact]
    public async Task ListBanks_Anonymous_Returns401NotPremium403()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // 401, never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }
}

/// <summary>
/// End-to-end HTTP test for the banks endpoint's "never fails" guarantee: with the stubbed VietQR directory
/// call returning HTTP 500, the endpoint still returns 200 with the committed static fallback list.
/// </summary>
[Collection("AuthIntegration")]
public class BanksEndpointFallbackTests(BanksProviderDownWebApplicationFactory factory, DatabaseFixture fixture)
    : TierEndpointTestBase(factory, fixture), IClassFixture<BanksProviderDownWebApplicationFactory>, IClassFixture<DatabaseFixture>
{
    private const string Route = "api/v1/banks";

    [SkippableFact]
    public async Task ListBanks_ProviderDown_Returns200WithStaticFallback()
    {
        var (client, _) = await CreateFreeClientAsync();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // provider 500 -> still 200
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());

        var banks = envelope.RootElement.GetProperty("data").EnumerateArray().ToList();
        // The static fallback carries the full committed snapshot (far larger than any stub), each with a logoUrl.
        Assert.True(banks.Count >= 50, $"expected the full static fallback, got {banks.Count} banks");
        Assert.All(banks, bank =>
        {
            Assert.False(string.IsNullOrWhiteSpace(bank.GetProperty("bin").GetString()));
            Assert.StartsWith("https://vietqr.vn/api/vietqr/images/", bank.GetProperty("logoUrl").GetString()!);
            Assert.DoesNotContain("imageId", bank.EnumerateObject().Select(property => property.Name));
        });
    }
}
