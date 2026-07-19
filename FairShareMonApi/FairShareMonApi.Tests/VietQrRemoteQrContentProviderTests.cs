using System.Net;
using FairShareMonApi.Models.Banks;
using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="VietQrRemoteQrContentProvider"/> over a real <see cref="VietQrApiClient"/>
/// (stubbed HTTP), a fake <see cref="IBankDirectoryService"/>, and the real <see cref="VietQrPayloadBuilder"/>
/// (no DB). Proves: the happy path returns the remote <c>qrCode</c>; a remote failure OR an unresolved
/// bankCode falls back to the local builder (byte-identical, never throws) and logs a warning; and the
/// unresolved-bankCode path short-circuits without an HTTP call.
/// </summary>
public class VietQrRemoteQrContentProviderTests
{
    private const string Bin = "970436";
    private const string Account = "0123456789";
    private const string Holder = "Nguyen Van A";

    private static readonly BankResponse KnownBank = new()
    {
        Bin = Bin, Code = "VCB", Name = "Vietcombank", ShortName = "Vietcombank", LogoUrl = "https://logo/x"
    };

    private readonly VietQrPayloadBuilder _builder = new();

    private static VietQrApiClient CreateApiClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new BanksOptions()),
            NullLogger<VietQrApiClient>.Instance);

    private static QrContentRequest Request() => new(Bin, Account, Holder, 500_000m, "Com trua");

    [Fact]
    public async Task BuildContentAsync_HappyPath_ReturnsRemoteQrCodeAndDoesNotWarn()
    {
        var directory = new FakeBankDirectoryService([KnownBank]);
        var handler = StubHttpMessageHandler.Json("""{ "qrCode": "REMOTE-VIETQR-STRING" }""");
        var logger = new CapturingLogger<VietQrRemoteQrContentProvider>();
        var provider = new VietQrRemoteQrContentProvider(CreateApiClient(handler), directory, _builder, logger);

        var content = await provider.BuildContentAsync(Request(), CancellationToken.None);

        Assert.Equal("REMOTE-VIETQR-STRING", content);
        Assert.False(logger.HasWarning);
    }

    [Fact]
    public async Task BuildContentAsync_RemoteFailure_FallsBackToLocalBuilderAndWarns()
    {
        var directory = new FakeBankDirectoryService([KnownBank]);
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError);
        var logger = new CapturingLogger<VietQrRemoteQrContentProvider>();
        var provider = new VietQrRemoteQrContentProvider(CreateApiClient(handler), directory, _builder, logger);

        var content = await provider.BuildContentAsync(Request(), CancellationToken.None);

        Assert.Equal(_builder.Build(Bin, Account, 500_000m, "Com trua"), content); // byte-identical to local
        Assert.True(logger.HasWarning);
    }

    [Fact]
    public async Task BuildContentAsync_RemoteReturnsNoQrCode_FallsBackToLocalBuilder()
    {
        var directory = new FakeBankDirectoryService([KnownBank]);
        var handler = StubHttpMessageHandler.Json("""{ "code": "00" }"""); // 200 but no qrCode -> null
        var logger = new CapturingLogger<VietQrRemoteQrContentProvider>();
        var provider = new VietQrRemoteQrContentProvider(CreateApiClient(handler), directory, _builder, logger);

        var content = await provider.BuildContentAsync(Request(), CancellationToken.None);

        Assert.Equal(_builder.Build(Bin, Account, 500_000m, "Com trua"), content);
        Assert.True(logger.HasWarning);
    }

    [Fact]
    public async Task BuildContentAsync_UnresolvedBankCode_FallsBackWithoutCallingRemote()
    {
        var directory = new FakeBankDirectoryService([]); // BIN not in the directory
        var handler = StubHttpMessageHandler.Json("""{ "qrCode": "SHOULD-NOT-BE-USED" }""");
        var logger = new CapturingLogger<VietQrRemoteQrContentProvider>();
        var provider = new VietQrRemoteQrContentProvider(CreateApiClient(handler), directory, _builder, logger);

        var content = await provider.BuildContentAsync(Request(), CancellationToken.None);

        Assert.Equal(_builder.Build(Bin, Account, 500_000m, "Com trua"), content);
        Assert.True(logger.HasWarning);
        Assert.Empty(handler.Requests); // short-circuited: no outbound generate call
    }

    private sealed class FakeBankDirectoryService(IReadOnlyList<BankResponse> banks) : IBankDirectoryService
    {
        public Task<IReadOnlyList<BankResponse>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(banks);
    }
}
