using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Models.Banks;
using Microsoft.Extensions.Options;

namespace FairShareMonApi.Services.Api.Banks;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for VietQR (registered via <c>AddHttpClient&lt;VietQrApiClient&gt;()</c>,
/// standard .NET wiring - NOT DiDecoration). This is the repo's first outbound HTTP call. No application
/// auth/locale headers are sent to the third party. <see cref="ListRawAsync"/> throws on any failure so the
/// directory service can fall back; <see cref="GenerateAsync"/> returns <c>null</c> on any failure so the QR
/// provider can fall back to the local builder.
/// </summary>
public sealed class VietQrApiClient(HttpClient http, IOptions<BanksOptions> options, ILogger<VietQrApiClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private VietQrOptions VietQr => options.Value.VietQr;

    /// <summary>
    /// GET the VietQR bank directory. Tolerates a bare array or a <c>{ data: [...] }</c> wrapper.
    /// Throws on a non-success status or an unreadable body (the directory service catches → fallback).
    /// </summary>
    public async Task<IReadOnlyList<VietQrRawBank>> ListRawAsync(CancellationToken cancellationToken)
    {
        var url = $"{VietQr.BaseUrl}{VietQr.BanksPath}";

        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var arrayElement = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement,
            JsonValueKind.Object when document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data,
            _ => throw new InvalidOperationException("VietQR directory response was not an array.")
        };

        var banks = arrayElement.Deserialize<List<VietQrRawBank>>(JsonOptions);
        return banks ?? throw new InvalidOperationException("VietQR directory response could not be deserialized.");
    }

    /// <summary>
    /// POST to VietQR to generate the QR content string. Returns <c>qrCode</c> / <c>data.qrCode</c>, or
    /// <c>null</c> on a non-success status, an unreadable body, or a missing QR string (caller falls back
    /// to the local builder).
    /// </summary>
    public async Task<string?> GenerateAsync(
        string bankCodeOrBin,
        string accountNo,
        string accountName,
        decimal amount,
        string? addInfo,
        CancellationToken cancellationToken)
    {
        var url = $"{VietQr.BaseUrl}{VietQr.GeneratePath}";
        var request = new VietQrGenerateRequest
        {
            BankCode = bankCodeOrBin,
            AccountNo = accountNo,
            AccountName = accountName,
            Amount = amount,
            AddInfo = addInfo
        };

        try
        {
            using var response = await http.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("VietQR generate returned non-success status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<VietQrGenerateResponse>(JsonOptions, cancellationToken);
            var qrCode = payload?.QrCode ?? payload?.Data?.QrCode;
            if (string.IsNullOrWhiteSpace(qrCode))
            {
                logger.LogWarning("VietQR generate response did not contain a qrCode string.");
                return null;
            }

            return qrCode;
        }
        // Gate on the PASSED token: a genuine caller cancellation rethrows, but the HttpClient.Timeout
        // (a TaskCanceledException) is a provider failure → null so the caller falls back to the local builder.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "VietQR generate call failed.");
            return null;
        }
    }
}
