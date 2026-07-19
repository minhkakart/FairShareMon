namespace FairShareMonApi.Services.Api.Banks;

/// <summary>Yêu cầu tạo nội dung QR chuyển khoản cho một tài khoản nhận.</summary>
public sealed record QrContentRequest(
    string BankBin,
    string AccountNumber,
    string AccountHolderName,
    decimal Amount,
    string? AddInfo);

/// <summary>
/// Nguồn tạo nội dung (chuỗi TLV) cho QR chuyển khoản. Có thể chọn qua cấu hình <c>Banks:QrProvider</c>;
/// việc dựng ảnh QR (QRCoder/SkiaSharp) không đổi. Mỗi hiện thực có một <see cref="Key"/> để phân giải.
/// </summary>
public interface IQrContentProvider
{
    /// <summary>Khóa định danh nhà cung cấp (khớp <c>Banks:QrProvider</c>, không phân biệt hoa thường).</summary>
    string Key { get; }

    /// <summary>Tạo chuỗi nội dung QR cho yêu cầu.</summary>
    Task<string> BuildContentAsync(QrContentRequest request, CancellationToken cancellationToken);
}
