using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiDecoration.Attributes;
using FairShareMonApi.Models.BankCallbacks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FairShareMonApi.Services.Api.BankCallbacks;

/// <summary>
/// First provider implementation (Decision Log entry 3): SePay's webhook. The exact auth scheme and
/// payload shape are UNVERIFIED assumptions (planning/bank-callback-settlement.md Assumptions) - only
/// this class needs correction if SePay's real contract differs. Auth is assumed to be a static API key
/// in an <c>Authorization: Apikey {key}</c> header, constant-time compared; the payload's own
/// pre-extracted <c>code</c> field is preferred over the app-side regex fallback, so this works whether
/// or not SePay's own content-extraction feature is configured for the integration.
/// </summary>
[ScopedService(typeof(IBankCallbackParser), Multiple = true)]
public sealed class SePayBankCallbackParser(IOptions<BankCallbacksOptions> options) : IBankCallbackParser
{
    private const string AuthorizationHeaderName = "Authorization";
    private const string AuthorizationScheme = "Apikey";
    private const string IncomingTransferType = "in";

    /// <summary>SePay's own <c>transactionDate</c> format, e.g. "2026-08-26 14:02:37".</summary>
    private const string TransactionDateFormat = "yyyy-MM-dd HH:mm:ss";

    public string ProviderKey => "sepay";

    public bool Verify(HttpRequest request, JsonElement payload)
    {
        var configuredKey = options.Value.SePay.ApiKey;
        // Missing/blank configured key always fails closed - never "no key configured = allow".
        if (string.IsNullOrWhiteSpace(configuredKey))
            return false;

        if (!request.Headers.TryGetValue(AuthorizationHeaderName, out var headerValues))
            return false;

        var expected = Encoding.UTF8.GetBytes($"{AuthorizationScheme} {configuredKey}");
        var actual = Encoding.UTF8.GetBytes(headerValues.ToString());

        // FixedTimeEquals requires equal-length spans; a length mismatch is a safe, immediate reject.
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public BankTransactionEvent? Parse(JsonElement payload)
    {
        if (!TryGetProviderTransactionId(payload, out var providerTransactionId))
            return null;

        var transferType = GetString(payload, "transferType");
        var isIncoming = string.Equals(transferType, IncomingTransferType, StringComparison.OrdinalIgnoreCase);

        var content = GetString(payload, "content") ?? string.Empty;
        var amount = GetDecimal(payload, "transferAmount");
        var destinationAccountNumber = GetString(payload, "accountNumber");
        // Assumption: transactionDate carries no timezone marker; treated as UTC (flagged alongside the
        // other unverified SePay contract assumptions - correct here only if wrong).
        var transactionAt = GetDateTime(payload, "transactionDate") ?? DateTime.UtcNow;

        var extractedCode = ExtractCode(payload, content);

        return new BankTransactionEvent(
            providerTransactionId,
            isIncoming,
            amount,
            content,
            extractedCode,
            transactionAt,
            BankBin: null,
            destinationAccountNumber);
    }

    /// <summary>Prefers SePay's own pre-extracted <c>code</c> field when non-empty, else falls back to the app-side prefix regex over <c>content</c>.</summary>
    private string? ExtractCode(JsonElement payload, string content)
    {
        var ownCode = GetString(payload, "code");
        if (!string.IsNullOrWhiteSpace(ownCode))
            return ownCode.Trim();

        var prefix = string.IsNullOrWhiteSpace(options.Value.SePay.CodePrefix)
            ? Database.Entities.QrCorrelationCode.CodePrefix
            : options.Value.SePay.CodePrefix;
        var pattern = Regex.Escape(prefix) + "[A-Z2-9]{6}";
        var match = Regex.Match(content, pattern);
        return match.Success ? match.Value : null;
    }

    private static bool TryGetProviderTransactionId(JsonElement payload, out string providerTransactionId)
    {
        providerTransactionId = string.Empty;
        if (!payload.TryGetProperty("id", out var idElement))
            return false;

        providerTransactionId = idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.GetRawText(),
            JsonValueKind.String => idElement.GetString() ?? string.Empty,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(providerTransactionId);
    }

    private static string? GetString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element))
            return null;

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static decimal GetDecimal(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element))
            return 0m;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m
        };
    }

    private static DateTime? GetDateTime(JsonElement payload, string propertyName)
    {
        var raw = GetString(payload, propertyName);
        if (raw is null)
            return null;

        if (DateTime.TryParseExact(raw, TransactionDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return DateTime.SpecifyKind(exact, DateTimeKind.Utc);

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? DateTime.SpecifyKind(fallback, DateTimeKind.Utc)
            : null;
    }
}
