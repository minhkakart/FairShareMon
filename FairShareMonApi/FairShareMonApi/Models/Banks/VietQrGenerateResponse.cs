using System.Text.Json.Serialization;

namespace FairShareMonApi.Models.Banks;

/// <summary>
/// Phản hồi từ VietQR khi tạo nội dung QR. Chuỗi QR có thể nằm trực tiếp ở <c>qrCode</c> hoặc lồng trong
/// <c>data.qrCode</c>; client dung nạp cả hai dạng.
/// </summary>
public class VietQrGenerateResponse
{
    /// <summary>Chuỗi nội dung QR (dạng phẳng).</summary>
    [JsonPropertyName("qrCode")]
    public string? QrCode { get; set; }

    /// <summary>Khối dữ liệu lồng (một số phản hồi bọc chuỗi QR trong đây).</summary>
    [JsonPropertyName("data")]
    public VietQrGenerateData? Data { get; set; }
}

/// <summary>Khối dữ liệu lồng chứa chuỗi nội dung QR.</summary>
public class VietQrGenerateData
{
    /// <summary>Chuỗi nội dung QR (dạng lồng).</summary>
    [JsonPropertyName("qrCode")]
    public string? QrCode { get; set; }
}
