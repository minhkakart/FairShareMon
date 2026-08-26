using DiDecoration.Attributes;

namespace FairShareMonApi.Services.Api.BankCallbacks;

/// <summary>Resolves the <see cref="IBankCallbackParser"/> for a <c>{provider}</c> route segment.</summary>
public interface IBankCallbackParserResolver
{
    /// <summary>
    /// Matches <paramref name="providerKey"/> against every registered parser's <see cref="IBankCallbackParser.ProviderKey"/>
    /// (case-insensitive). Returns null on no match (Decision Log entry 8) - unlike
    /// <c>QrContentProviderResolver</c>'s always-fallback-to-local design, there is no sensible default
    /// bank-transaction aggregator for an inbound webhook.
    /// </summary>
    IBankCallbackParser? Resolve(string providerKey);
}

[ScopedService(typeof(IBankCallbackParserResolver))]
public sealed class BankCallbackParserResolver(IEnumerable<IBankCallbackParser> parsers) : IBankCallbackParserResolver
{
    public IBankCallbackParser? Resolve(string providerKey) =>
        parsers.FirstOrDefault(parser => string.Equals(parser.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));
}
