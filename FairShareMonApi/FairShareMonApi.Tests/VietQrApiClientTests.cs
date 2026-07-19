using System.Net;
using FairShareMonApi.Models.Banks;
using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the typed <see cref="VietQrApiClient"/> over a stubbed <see cref="HttpMessageHandler"/>
/// (no network). Proves the directory read tolerates both a bare array and a <c>{ data: [...] }</c> wrapper
/// and throws on failure (so the directory service can fall back), that generate returns
/// <c>qrCode</c>/<c>data.qrCode</c> and yields <c>null</c> on any failure (so the QR provider can fall back),
/// and that NO application auth/locale headers are sent to the third party.
/// </summary>
public class VietQrApiClientTests
{
    private static readonly BanksOptions Options = new()
    {
        VietQr = new VietQrOptions
        {
            BaseUrl = "https://vietqr.test",
            BanksPath = "/api/vietqr/banks",
            GeneratePath = "/api/vietqr/generate"
        }
    };

    private static VietQrApiClient CreateClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options), NullLogger<VietQrApiClient>.Instance);

    // ---- ListRawAsync -----------------------------------------------------------------------------

    [Fact]
    public async Task ListRawAsync_BareArray_ParsesAllEntries()
    {
        const string json = """
        [
          { "caiValue": "970436", "bankCode": "VCB", "bankName": "Vietcombank", "bankShortName": "VCB", "imageId": "img-vcb" },
          { "caiValue": "970422", "bankCode": "MB", "bankName": "MB Bank", "bankShortName": "MBBank", "imageId": "img-mb" }
        ]
        """;
        var client = CreateClient(StubHttpMessageHandler.Json(json));

        var banks = await client.ListRawAsync(CancellationToken.None);

        Assert.Equal(2, banks.Count);
        Assert.Equal("970436", banks[0].CaiValue);
        Assert.Equal("img-mb", banks[1].ImageId);
    }

    [Fact]
    public async Task ListRawAsync_DataWrapper_ParsesInnerArray()
    {
        const string json = """
        { "code": "00", "data": [ { "caiValue": "970407", "bankCode": "TCB", "bankName": "Techcombank", "bankShortName": "Techcombank", "imageId": "img-tcb" } ] }
        """;
        var client = CreateClient(StubHttpMessageHandler.Json(json));

        var banks = await client.ListRawAsync(CancellationToken.None);

        Assert.Single(banks);
        Assert.Equal("970407", banks[0].CaiValue);
    }

    [Fact]
    public async Task ListRawAsync_NonSuccessStatus_Throws()
    {
        var client = CreateClient(StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAnyAsync<Exception>(() => client.ListRawAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListRawAsync_UnexpectedShape_Throws()
    {
        // Neither a bare array nor a { data: [...] } wrapper -> the client rejects it.
        var client = CreateClient(StubHttpMessageHandler.Json("""{ "message": "nope" }"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListRawAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListRawAsync_SendsNoApplicationHeaders()
    {
        var handler = StubHttpMessageHandler.Json("[]");
        var client = CreateClient(handler);

        await client.ListRawAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        AssertNoAppHeaders(request);
        Assert.Equal("https://vietqr.test/api/vietqr/banks", request.RequestUri!.ToString());
    }

    // ---- GenerateAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_FlatQrCode_ReturnsIt()
    {
        var client = CreateClient(StubHttpMessageHandler.Json("""{ "qrCode": "FLAT-QR-STRING" }"""));

        var qr = await client.GenerateAsync("VCB", "0123456789", "Nguyen Van A", 500_000m, "Com trua", CancellationToken.None);

        Assert.Equal("FLAT-QR-STRING", qr);
    }

    [Fact]
    public async Task GenerateAsync_DataWrappedQrCode_ReturnsIt()
    {
        var client = CreateClient(StubHttpMessageHandler.Json("""{ "data": { "qrCode": "NESTED-QR-STRING" } }"""));

        var qr = await client.GenerateAsync("VCB", "0123456789", "Nguyen Van A", 500_000m, null, CancellationToken.None);

        Assert.Equal("NESTED-QR-STRING", qr);
    }

    [Fact]
    public async Task GenerateAsync_NonSuccessStatus_ReturnsNull()
    {
        var client = CreateClient(StubHttpMessageHandler.Status(HttpStatusCode.BadGateway));

        var qr = await client.GenerateAsync("VCB", "0123456789", "Nguyen Van A", 1m, null, CancellationToken.None);

        Assert.Null(qr);
    }

    [Fact]
    public async Task GenerateAsync_MissingQrCode_ReturnsNull()
    {
        var client = CreateClient(StubHttpMessageHandler.Json("""{ "code": "00" }"""));

        var qr = await client.GenerateAsync("VCB", "0123456789", "Nguyen Van A", 1m, null, CancellationToken.None);

        Assert.Null(qr);
    }

    [Fact]
    public async Task GenerateAsync_TransportThrows_ReturnsNull()
    {
        var client = CreateClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("boom")));

        var qr = await client.GenerateAsync("VCB", "0123456789", "Nguyen Van A", 1m, null, CancellationToken.None);

        Assert.Null(qr);
    }

    [Fact]
    public async Task GenerateAsync_SendsNoApplicationHeaders()
    {
        var handler = StubHttpMessageHandler.Json("""{ "qrCode": "X" }""");
        var client = CreateClient(handler);

        await client.GenerateAsync("VCB", "0123456789", "Nguyen Van A", 1m, null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        AssertNoAppHeaders(request);
        Assert.Equal("https://vietqr.test/api/vietqr/generate", request.RequestUri!.ToString());
    }

    private static void AssertNoAppHeaders(HttpRequestMessage request)
    {
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.False(request.Headers.Contains("Accept-Language"));
        Assert.False(request.Headers.Contains("X-Time-Zone"));
    }
}
