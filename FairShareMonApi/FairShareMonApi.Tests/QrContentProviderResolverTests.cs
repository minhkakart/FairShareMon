using FairShareMonApi.Models.Banks;
using FairShareMonApi.Services.Api.Banks;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="QrContentProviderResolver"/> (no DB, no HTTP). Proves selection by the
/// <c>Banks:QrProvider</c> config: the "local" provider by default / unset / unknown key, and the "vietqr"
/// provider when configured (case-insensitively). The resolver never returns null.
/// </summary>
public class QrContentProviderResolverTests
{
    private static IQrContentProviderResolver CreateResolver(string? configuredKey)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new BanksOptions { QrProvider = configuredKey! });
        IEnumerable<IQrContentProvider> providers = [new FakeProvider("local"), new FakeProvider("vietqr")];
        return new QrContentProviderResolver(providers, options);
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("local")]
    [InlineData("")]
    [InlineData("something-unknown")]
    public void Resolve_DefaultUnsetOrUnknown_ReturnsLocalProvider(string configuredKey)
    {
        Assert.Equal("local", CreateResolver(configuredKey).Resolve().Key);
    }

    [Theory]
    [InlineData("VietQr")]
    [InlineData("vietqr")]
    [InlineData("VIETQR")]
    public void Resolve_VietQrConfigured_ReturnsVietQrProviderCaseInsensitively(string configuredKey)
    {
        Assert.Equal("vietqr", CreateResolver(configuredKey).Resolve().Key);
    }

    [Fact]
    public void Resolve_NullConfiguredKey_FallsBackToLocal()
    {
        Assert.Equal("local", CreateResolver(null).Resolve().Key);
    }

    private sealed class FakeProvider(string key) : IQrContentProvider
    {
        public string Key => key;

        public Task<string> BuildContentAsync(QrContentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(key);
    }
}
