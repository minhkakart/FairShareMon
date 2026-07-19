using System.Text.RegularExpressions;
using DiDecoration.Attributes;
using FairShareMonApi.Models.Banks;
using Microsoft.Extensions.Options;

namespace FairShareMonApi.Services.Api.Banks;

/// <summary>Một mục ngân hàng đã chuẩn hóa từ nhà cung cấp danh mục (giữ <c>ImageId</c> nội bộ).</summary>
public sealed record ProviderBank(string Bin, string Code, string Name, string ShortName, string ImageId);

/// <summary>
/// Nguồn cung cấp danh mục ngân hàng (VietQR là một hiện thực). Trả về danh sách <see cref="ProviderBank"/>
/// đã chuẩn hóa và dựng URL logo từ mã ảnh.
/// </summary>
public interface IBankDirectoryProvider
{
    /// <summary>Lấy danh mục ngân hàng đã chuẩn hóa từ nguồn.</summary>
    Task<IReadOnlyList<ProviderBank>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Dựng URL logo đầy đủ từ mã ảnh.</summary>
    string BuildLogoUrl(string imageId);
}

/// <summary>
/// Hiện thực <see cref="IBankDirectoryProvider"/> trên VietQR: gọi <see cref="VietQrApiClient.ListRawAsync"/>,
/// chuẩn hóa từng mục thô (caiValue→Bin, bankCode→Code, bankName→Name, bankShortName→ShortName, imageId),
/// cắt khoảng trắng và loại bỏ mọi mục có BIN không khớp <c>^\d{6}$</c> (giống quy tắc phía web).
/// </summary>
[ScopedService(typeof(IBankDirectoryProvider))]
public sealed partial class VietQrBankDirectoryProvider(VietQrApiClient client, IOptions<BanksOptions> options) : IBankDirectoryProvider
{
    private VietQrOptions VietQr => options.Value.VietQr;

    public async Task<IReadOnlyList<ProviderBank>> ListAsync(CancellationToken cancellationToken)
    {
        var raw = await client.ListRawAsync(cancellationToken);

        return raw
            .Select(entry => new ProviderBank(
                (entry.CaiValue ?? string.Empty).Trim(),
                (entry.BankCode ?? string.Empty).Trim(),
                (entry.BankName ?? string.Empty).Trim(),
                (entry.BankShortName ?? string.Empty).Trim(),
                (entry.ImageId ?? string.Empty).Trim()))
            .Where(bank => BinPattern().IsMatch(bank.Bin))
            .ToList();
    }

    public string BuildLogoUrl(string imageId) => $"{VietQr.BaseUrl}{VietQr.ImagePath}/{imageId}";

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex BinPattern();
}
