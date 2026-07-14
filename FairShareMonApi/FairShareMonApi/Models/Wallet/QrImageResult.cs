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

/// <summary>
/// Kết quả tạo QR cho một phiếu: hoặc ảnh PNG (mặc định) hoặc chuỗi payload VietQR thô
/// (khi <c>format=payload</c>, trả về trong <c>ApiResult&lt;string&gt;</c>) - OQ10.
/// </summary>
public sealed class ExpenseQrResult
{
    private ExpenseQrResult(string? payload, QrImageResult? image)
    {
        Payload = payload;
        Image = image;
    }

    /// <summary>Chuỗi payload VietQR thô; khác null khi client yêu cầu <c>format=payload</c>.</summary>
    public string? Payload { get; }

    /// <summary>Ảnh QR PNG; khác null ở chế độ mặc định.</summary>
    public QrImageResult? Image { get; }

    /// <summary>True nếu là chế độ trả về chuỗi payload thô.</summary>
    public bool IsPayload => Payload is not null;

    public static ExpenseQrResult FromPayload(string payload) => new(payload, null);

    public static ExpenseQrResult FromImage(QrImageResult image) => new(null, image);
}
