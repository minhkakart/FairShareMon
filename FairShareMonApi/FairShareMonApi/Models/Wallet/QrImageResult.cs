namespace FairShareMonApi.Models.Wallet;

/// <summary>
/// Kết quả tạo ảnh QR do <c>IWalletQrService</c> trả về: nội dung byte (PNG) cùng content-type và tên
/// tệp, để controller đổ thẳng ra <c>File(...)</c> (bỏ qua bao bọc <c>ApiResult</c> - M8, OQ10).
/// </summary>
public sealed class QrImageResult(byte[] content, string contentType, string fileName)
{
    /// <summary>Nội dung ảnh QR đã kết xuất (PNG).</summary>
    public byte[] Content { get; } = content;

    /// <summary>Content-Type của ảnh, ví dụ <c>image/png</c>.</summary>
    public string ContentType { get; } = contentType;

    /// <summary>Tên tệp tải về (ASCII an toàn).</summary>
    public string FileName { get; } = fileName;
}
