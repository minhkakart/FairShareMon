using FairShareMonApi.Models.Banks;
using FairShareMonApi.Services.Api.Banks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="BankDirectoryService"/> over a fake <see cref="IBankDirectoryProvider"/>
/// and a REAL <see cref="MemoryCache"/> (no DB, no HTTP). Proves: a successful provider result is mapped
/// to <see cref="BankResponse"/> with <c>LogoUrl</c> built via <c>provider.BuildLogoUrl(imageId)</c> (the
/// imageId itself is never surfaced as its own field) and cached under key <c>banks:list</c>; a second call
/// within TTL is served from cache (the provider is invoked exactly once); and — per OQ-B(a) — a provider
/// failure with a cold cache returns the static fallback UNCACHED, so the next call retries the provider
/// and self-heals.
/// </summary>
public class BankDirectoryServiceTests
{
    private const string CacheKey = "banks:list";

    private static readonly ProviderBank SampleBank = new("970436", "VCB", "Ngân hàng Ngoại thương", "Vietcombank", "img-vcb");

    private static BankDirectoryService CreateService(IBankDirectoryProvider provider, IMemoryCache cache) =>
        new(provider, cache, NullLogger<BankDirectoryService>.Instance);

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task ListAsync_SuccessfulProvider_MapsWithBuiltLogoUrlAndCachesUnderBanksList()
    {
        var provider = new FakeBankDirectoryProvider { Banks = [SampleBank] };
        using var cache = NewCache();

        var result = await CreateService(provider, cache).ListAsync(CancellationToken.None);

        var bank = Assert.Single(result);
        Assert.Equal("970436", bank.Bin);
        Assert.Equal("VCB", bank.Code);
        Assert.Equal("Vietcombank", bank.ShortName);
        // LogoUrl is built via BuildLogoUrl(imageId); the imageId is folded into the URL, never leaked as its own field.
        Assert.Equal("https://logo.test/img-vcb", bank.LogoUrl);

        // The successful result lives in the cache under the documented key.
        Assert.True(cache.TryGetValue(CacheKey, out IReadOnlyList<BankResponse>? cached));
        Assert.Same(result, cached);
    }

    [Fact]
    public async Task ListAsync_SecondCallWithinTtl_ServedFromCacheProviderInvokedOnce()
    {
        var provider = new FakeBankDirectoryProvider { Banks = [SampleBank] };
        using var cache = NewCache();
        var service = CreateService(provider, cache);

        var first = await service.ListAsync(CancellationToken.None);
        var second = await service.ListAsync(CancellationToken.None);

        Assert.Equal(1, provider.ListCallCount); // second call did not hit the provider
        Assert.Same(first, second);
    }

    [Fact]
    public async Task ListAsync_ProviderThrowsColdCache_ReturnsNonEmptyStaticFallback()
    {
        var provider = new FakeBankDirectoryProvider { Throw = true };
        using var cache = NewCache();

        var result = await CreateService(provider, cache).ListAsync(CancellationToken.None);

        Assert.NotEmpty(result);                          // the committed static fallback is non-empty
        Assert.All(result, bank => Assert.False(string.IsNullOrWhiteSpace(bank.LogoUrl)));
    }

    [Fact]
    public async Task ListAsync_ProviderThrowsColdCache_DoesNotCacheTheFallback()
    {
        var provider = new FakeBankDirectoryProvider { Throw = true };
        using var cache = NewCache();

        await CreateService(provider, cache).ListAsync(CancellationToken.None);

        // OQ-B(a): the fallback is served UNCACHED so the endpoint self-heals on the next call.
        Assert.False(cache.TryGetValue(CacheKey, out _));
    }

    [Fact]
    public async Task ListAsync_AfterFailure_RetriesProviderAndSelfHeals()
    {
        var provider = new FakeBankDirectoryProvider { Throw = true };
        using var cache = NewCache();
        var service = CreateService(provider, cache);

        // First call: provider down -> static fallback (uncached).
        var fallback = await service.ListAsync(CancellationToken.None);
        Assert.Equal(1, provider.ListCallCount);

        // Provider recovers; the next call must retry it (cache stayed cold) and return the live result.
        provider.Throw = false;
        provider.Banks = [SampleBank];
        var healed = await service.ListAsync(CancellationToken.None);

        Assert.Equal(2, provider.ListCallCount);          // retried, not stuck on the fallback
        var bank = Assert.Single(healed);
        Assert.Equal("https://logo.test/img-vcb", bank.LogoUrl);
        Assert.NotEqual(fallback.Count, healed.Count);    // the healed live list differs from the fallback
    }

    private sealed class FakeBankDirectoryProvider : IBankDirectoryProvider
    {
        public IReadOnlyList<ProviderBank> Banks { get; set; } = [];
        public bool Throw { get; set; }
        public int ListCallCount { get; private set; }

        public Task<IReadOnlyList<ProviderBank>> ListAsync(CancellationToken cancellationToken)
        {
            ListCallCount++;
            if (Throw)
                throw new InvalidOperationException("provider down (test double)");
            return Task.FromResult(Banks);
        }

        public string BuildLogoUrl(string imageId) => $"https://logo.test/{imageId}";
    }
}
