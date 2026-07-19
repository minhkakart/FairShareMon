using DiDecoration.Attributes;
using FairShareMonApi.Services.Api.Wallet;

namespace FairShareMonApi.Services.Api.Banks;

/// <summary>
/// Nhà cung cấp nội dung QR "vietqr" (tùy chọn): POST tới <c>/api/vietqr/generate</c> và trả <c>qrCode</c>.
/// Phân giải mã ngân hàng từ danh mục theo BIN; nếu không phân giải được, hoặc gọi từ xa lỗi/không có kết quả,
/// thì rơi về builder cục bộ (ghi cảnh báo) nên QR không bao giờ hỏng.
/// </summary>
[ScopedService(typeof(IQrContentProvider), Multiple = true)]
public sealed class VietQrRemoteQrContentProvider(
    VietQrApiClient client,
    IBankDirectoryService directory,
    IVietQrPayloadBuilder builder,
    ILogger<VietQrRemoteQrContentProvider> logger) : IQrContentProvider
{
    public string Key => "vietqr";

    public async Task<string> BuildContentAsync(QrContentRequest request, CancellationToken cancellationToken)
    {
        var banks = await directory.ListAsync(cancellationToken);
        var bankCode = banks.FirstOrDefault(bank => bank.Bin == request.BankBin)?.Code;
        if (string.IsNullOrWhiteSpace(bankCode))
        {
            logger.LogWarning("VietQR content provider could not resolve a bankCode for BIN {Bin}; falling back to the local builder.", request.BankBin);
            return BuildLocal(request);
        }

        var remote = await client.GenerateAsync(
            bankCode, request.AccountNumber, request.AccountHolderName, request.Amount, request.AddInfo, cancellationToken);
        if (string.IsNullOrWhiteSpace(remote))
        {
            logger.LogWarning("VietQR generate returned no content for BIN {Bin}; falling back to the local builder.", request.BankBin);
            return BuildLocal(request);
        }

        return remote;
    }

    private string BuildLocal(QrContentRequest request) =>
        builder.Build(request.BankBin, request.AccountNumber, request.Amount, request.AddInfo);
}
