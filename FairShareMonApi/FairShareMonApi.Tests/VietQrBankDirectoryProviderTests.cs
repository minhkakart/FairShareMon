using FairShareMonApi.Models.Banks;
using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="VietQrBankDirectoryProvider"/> over a real <see cref="VietQrApiClient"/>
/// backed by a stubbed HTTP handler (no network). Proves raw VietQR entries are normalized
/// (caiValue→Bin, bankCode→Code, bankName→Name, bankShortName→ShortName, imageId), trimmed, and that any
/// entry whose BIN fails <c>^\d{6}$</c> is dropped; and that <c>BuildLogoUrl</c> composes
/// <c>{BaseUrl}{ImagePath}/{imageId}</c>.
/// </summary>
public class VietQrBankDirectoryProviderTests
{
    private static VietQrBankDirectoryProvider CreateProvider(string json, BanksOptions? options = null)
    {
        options ??= new BanksOptions();
        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        var client = new VietQrApiClient(new HttpClient(StubHttpMessageHandler.Json(json)), wrapped, NullLogger<VietQrApiClient>.Instance);
        return new VietQrBankDirectoryProvider(client, wrapped);
    }

    [Fact]
    public async Task ListAsync_MapsRawFieldsAndTrimsWhitespace()
    {
        const string json = """
        [ { "caiValue": " 970436 ", "bankCode": " VCB ", "bankName": " Vietcombank ", "bankShortName": " VCB-Short ", "imageId": " img-vcb " } ]
        """;
        var provider = CreateProvider(json);

        var bank = Assert.Single(await provider.ListAsync(CancellationToken.None));

        Assert.Equal("970436", bank.Bin);
        Assert.Equal("VCB", bank.Code);
        Assert.Equal("Vietcombank", bank.Name);
        Assert.Equal("VCB-Short", bank.ShortName);
        Assert.Equal("img-vcb", bank.ImageId);
    }

    [Fact]
    public async Task ListAsync_DropsEntriesWhoseBinIsNotSixDigits()
    {
        const string json = """
        [
          { "caiValue": "970436", "bankCode": "VCB",  "bankName": "OK6",       "bankShortName": "OK6",  "imageId": "a" },
          { "caiValue": "12345",  "bankCode": "SHORT","bankName": "FiveDigits", "bankShortName": "S5",   "imageId": "b" },
          { "caiValue": "1234567","bankCode": "LONG", "bankName": "SevenDigits","bankShortName": "S7",   "imageId": "c" },
          { "caiValue": "97A436", "bankCode": "ALPHA","bankName": "HasLetter",  "bankShortName": "AL",   "imageId": "d" },
          { "caiValue": null,     "bankCode": "NULL", "bankName": "NullBin",    "bankShortName": "NB",   "imageId": "e" }
        ]
        """;
        var provider = CreateProvider(json);

        var banks = await provider.ListAsync(CancellationToken.None);

        var kept = Assert.Single(banks);
        Assert.Equal("970436", kept.Bin);
    }

    [Fact]
    public async Task ListAsync_EmptyDirectory_ReturnsEmpty()
    {
        var provider = CreateProvider("[]");

        Assert.Empty(await provider.ListAsync(CancellationToken.None));
    }

    [Fact]
    public void BuildLogoUrl_ComposesBaseUrlImagePathAndImageId()
    {
        var provider = CreateProvider("[]", new BanksOptions
        {
            VietQr = new VietQrOptions { BaseUrl = "https://vietqr.vn", ImagePath = "/api/vietqr/images" }
        });

        Assert.Equal("https://vietqr.vn/api/vietqr/images/img-123", provider.BuildLogoUrl("img-123"));
    }
}
