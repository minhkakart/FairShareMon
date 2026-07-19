using DiDecoration.Attributes;
using FairShareMonApi.Models.Banks;
using Microsoft.Extensions.Caching.Memory;

namespace FairShareMonApi.Services.Api.Banks;

/// <summary>
/// Danh mục ngân hàng cho client: bọc <see cref="IBankDirectoryProvider"/> bằng bộ nhớ đệm 24h và một
/// snapshot tĩnh dự phòng nên endpoint không bao giờ lỗi; ánh xạ <see cref="ProviderBank"/> sang
/// <see cref="BankResponse"/> (dựng URL logo, không lộ mã ảnh).
/// </summary>
public interface IBankDirectoryService
{
    /// <summary>Lấy danh mục ngân hàng đã sẵn sàng cho client.</summary>
    Task<IReadOnlyList<BankResponse>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Hiện thực <see cref="IBankDirectoryService"/>: chỉ lưu đệm kết quả thành công từ nhà cung cấp
/// (key <c>banks:list</c>, TTL 24h). Khi nhà cung cấp lỗi và đệm rỗng thì trả snapshot tĩnh nhưng
/// KHÔNG lưu vào đệm (OQ-B (a)) - lần gọi sau sẽ thử lại nhà cung cấp và tự phục hồi.
/// </summary>
[ScopedService(typeof(IBankDirectoryService))]
public sealed class BankDirectoryService(
    IBankDirectoryProvider provider,
    IMemoryCache cache,
    ILogger<BankDirectoryService> logger) : IBankDirectoryService
{
    private const string CacheKey = "banks:list";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<BankResponse>> ListAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<BankResponse>? cached) && cached is not null)
            return cached;

        try
        {
            var banks = await provider.ListAsync(cancellationToken);
            var mapped = Map(banks);

            // An empty result (HTTP 200 with [] / schema drift dropping every entry) is a provider miss,
            // not a valid directory: serve the fallback uncached rather than poisoning the cache for 24h.
            if (mapped.Count == 0)
            {
                logger.LogWarning("Bank directory provider returned an empty list; serving the static fallback (uncached).");
                return Map(BankDirectoryFallback.Snapshot);
            }

            cache.Set(CacheKey, mapped, CacheTtl);
            return mapped;
        }
        // Gate on the PASSED token: a genuine caller cancellation rethrows, but the HttpClient.Timeout
        // (a TaskCanceledException, which is an OperationCanceledException) is a provider failure → fallback.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Bank directory provider failed; serving the static fallback (uncached).");
            // OQ-B (a): serve the fallback WITHOUT caching it, so the next call retries the provider.
            return Map(BankDirectoryFallback.Snapshot);
        }
    }

    private IReadOnlyList<BankResponse> Map(IReadOnlyList<ProviderBank> banks) =>
        banks
            .Select(bank => new BankResponse
            {
                Bin = bank.Bin,
                Code = bank.Code,
                Name = bank.Name,
                ShortName = bank.ShortName,
                LogoUrl = provider.BuildLogoUrl(bank.ImageId)
            })
            .ToList();
}
