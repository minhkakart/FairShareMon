using DiDecoration.Attributes;
using FairShareMonApi.Models.Banks;
using Microsoft.Extensions.Options;

namespace FairShareMonApi.Services.Api.Banks;

/// <summary>Chọn nhà cung cấp nội dung QR theo cấu hình <c>Banks:QrProvider</c>.</summary>
public interface IQrContentProviderResolver
{
    /// <summary>Trả về nhà cung cấp khớp cấu hình; không khớp/thiếu thì trả nhà cung cấp "local" (không bao giờ null).</summary>
    IQrContentProvider Resolve();
}

/// <summary>
/// Hiện thực <see cref="IQrContentProviderResolver"/>: khớp <see cref="IQrContentProvider.Key"/> với
/// <c>Banks:QrProvider</c> (không phân biệt hoa thường); không khớp hoặc thiếu thì trả nhà cung cấp "local".
/// </summary>
[ScopedService(typeof(IQrContentProviderResolver))]
public sealed class QrContentProviderResolver(
    IEnumerable<IQrContentProvider> providers,
    IOptions<BanksOptions> options) : IQrContentProviderResolver
{
    private const string LocalKey = "local";

    public IQrContentProvider Resolve()
    {
        var configured = options.Value.QrProvider;
        var providerList = providers as IReadOnlyList<IQrContentProvider> ?? providers.ToList();

        return providerList.FirstOrDefault(provider => string.Equals(provider.Key, configured, StringComparison.OrdinalIgnoreCase))
            ?? providerList.First(provider => string.Equals(provider.Key, LocalKey, StringComparison.OrdinalIgnoreCase));
    }
}
