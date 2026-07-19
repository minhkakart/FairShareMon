using System.Net;
using FairShareMonApi.Services.Api.Banks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Test host for the banks endpoint whose outbound VietQR HTTP is STUBBED with a fixed 3-entry directory
/// (one carrying a non-6-digit BIN, so the drop rule is exercised end-to-end) and whose <c>Banks</c> options
/// are pinned to deterministic URLs. The real vietqr.vn is never contacted.
/// </summary>
public sealed class BanksStubWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Two valid banks (VCB, MB) + one dropped entry (5-digit BIN). imageId maps into logoUrl only.</summary>
    public const string DirectoryJson = """
    [
      { "caiValue": "970436", "bankCode": "VCB", "bankName": "Ngân hàng TMCP Ngoại thương Việt Nam", "bankShortName": "Vietcombank", "imageId": "img-vcb" },
      { "caiValue": "970422", "bankCode": "MB",  "bankName": "Ngân hàng TMCP Quân đội",              "bankShortName": "MBBank",      "imageId": "img-mb" },
      { "caiValue": "12345",  "bankCode": "BAD", "bankName": "Ngân hàng BIN không hợp lệ",           "bankShortName": "BadBank",     "imageId": "img-bad" }
    ]
    """;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(BanksConfig));
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<VietQrApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => StubHttpMessageHandler.Json(DirectoryJson)));
    }

    internal static readonly Dictionary<string, string?> BanksConfig = new()
    {
        ["Banks:QrProvider"] = "Local",
        ["Banks:VietQr:BaseUrl"] = "https://vietqr.vn",
        ["Banks:VietQr:BanksPath"] = "/api/vietqr/banks",
        ["Banks:VietQr:GeneratePath"] = "/api/vietqr/generate",
        ["Banks:VietQr:ImagePath"] = "/api/vietqr/images"
    };
}

/// <summary>
/// Test host whose stubbed VietQR directory call always returns HTTP 500, forcing the bank-directory
/// service down its committed static-fallback path — so the endpoint's "never fails" guarantee can be
/// proved over HTTP.
/// </summary>
public sealed class BanksProviderDownWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(BanksStubWebApplicationFactory.BanksConfig));
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<VietQrApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError)));
    }
}
