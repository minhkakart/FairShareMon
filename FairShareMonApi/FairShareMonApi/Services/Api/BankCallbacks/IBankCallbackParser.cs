using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace FairShareMonApi.Services.Api.BankCallbacks;

/// <summary>
/// One inbound bank transaction, normalized from a provider's raw webhook payload
/// (planning/bank-callback-settlement.md Step 4). <see cref="IsIncoming"/> false means the
/// transaction was ignored at parse time (e.g. SePay's own outbound "out" transfers) - the applier
/// short-circuits on this without any lookup.
/// </summary>
public sealed record BankTransactionEvent(
    string ProviderTransactionId,
    bool IsIncoming,
    decimal Amount,
    string Content,
    string? ExtractedCode,
    DateTime TransactionAt,
    string? BankBin,
    string? DestinationAccountNumber);

/// <summary>
/// Provider-pluggable inbound bank-transaction webhook parser (Decision Log entry 3). One
/// implementation per aggregator (first: <see cref="SePayBankCallbackParser"/>), registered
/// <c>Multiple = true</c> and matched by <see cref="ProviderKey"/> via <see cref="IBankCallbackParserResolver"/>.
/// </summary>
public interface IBankCallbackParser
{
    /// <summary>The route-segment key this parser answers to (e.g. "sepay"), matched case-insensitively.</summary>
    string ProviderKey { get; }

    /// <summary>
    /// Verifies the provider's own credential (API key/signature) carried on the request - the webhook's
    /// authorization surface is NOT the app's opaque-token scheme (Background). Missing/blank configured
    /// secret always fails closed.
    /// </summary>
    bool Verify(HttpRequest request, JsonElement payload);

    /// <summary>Normalizes the raw JSON payload into a <see cref="BankTransactionEvent"/>; null on a payload shape this parser cannot make sense of.</summary>
    BankTransactionEvent? Parse(JsonElement payload);
}
