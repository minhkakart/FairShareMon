using System.Text.Json;
using FairShareMonApi.Services.Api.BankCallbacks;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="BankCallbackParserResolver"/> (no DB) - planning/
/// bank-callback-settlement.md Step 10. Proves the resolver matches <c>ProviderKey</c>
/// case-insensitively and returns null (never a fallback) on an unknown provider (Decision Log entry 8 -
/// unlike <c>QrContentProviderResolver</c>'s always-fallback-to-local design).
/// </summary>
public class BankCallbackParserResolverTests
{
    private sealed class FakeParser(string providerKey) : IBankCallbackParser
    {
        public string ProviderKey => providerKey;
        public bool Verify(HttpRequest request, JsonElement payload) => true;
        public BankTransactionEvent? Parse(JsonElement payload) => null;
    }

    [Fact]
    public void Resolve_ExactCaseMatch_ReturnsTheMatchingParser()
    {
        var sepay = new FakeParser("sepay");
        var resolver = new BankCallbackParserResolver([sepay, new FakeParser("otherbank")]);

        Assert.Same(sepay, resolver.Resolve("sepay"));
    }

    [Theory]
    [InlineData("SePay")]
    [InlineData("SEPAY")]
    [InlineData("sEpAy")]
    public void Resolve_CaseInsensitive_MatchesRegardlessOfCasing(string providerKey)
    {
        var sepay = new FakeParser("sepay");
        var resolver = new BankCallbackParserResolver([sepay]);

        Assert.Same(sepay, resolver.Resolve(providerKey));
    }

    [Fact]
    public void Resolve_UnknownProvider_ReturnsNullNotAFallback()
    {
        var resolver = new BankCallbackParserResolver([new FakeParser("sepay")]);

        Assert.Null(resolver.Resolve("unknown-provider"));
    }

    [Fact]
    public void Resolve_NoParsersRegistered_ReturnsNull()
    {
        var resolver = new BankCallbackParserResolver([]);

        Assert.Null(resolver.Resolve("sepay"));
    }

    [Fact]
    public void Resolve_MultipleProvidersRegistered_PicksTheRightOneOnly()
    {
        var sepay = new FakeParser("sepay");
        var other = new FakeParser("otherbank");
        var resolver = new BankCallbackParserResolver([sepay, other]);

        Assert.Same(other, resolver.Resolve("otherbank"));
        Assert.Same(sepay, resolver.Resolve("sepay"));
    }
}
