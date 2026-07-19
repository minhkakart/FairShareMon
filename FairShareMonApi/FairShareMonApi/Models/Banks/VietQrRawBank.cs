using System.Text.Json.Serialization;

namespace FairShareMonApi.Models.Banks;

/// <summary>
/// DTO thô của một mục trong danh mục ngân hàng VietQR (dùng để giải tuần tự phản hồi từ VietQR).
/// Chỉ dùng nội bộ ở tầng client HTTP; được chuẩn hóa thành <see cref="ProviderBank"/> trước khi ra ngoài.
/// </summary>
public class VietQrRawBank
{
    /// <summary>Mã BIN của ngân hàng (chuẩn hóa thành <c>Bin</c>).</summary>
    [JsonPropertyName("caiValue")]
    public string? CaiValue { get; set; }

    /// <summary>Mã ngắn của ngân hàng (chuẩn hóa thành <c>Code</c>).</summary>
    [JsonPropertyName("bankCode")]
    public string? BankCode { get; set; }

    /// <summary>Tên đầy đủ của ngân hàng (chuẩn hóa thành <c>Name</c>).</summary>
    [JsonPropertyName("bankName")]
    public string? BankName { get; set; }

    /// <summary>Tên viết tắt của ngân hàng (chuẩn hóa thành <c>ShortName</c>).</summary>
    [JsonPropertyName("bankShortName")]
    public string? BankShortName { get; set; }

    /// <summary>Mã ảnh logo (dùng để dựng URL logo; không trả ra client).</summary>
    [JsonPropertyName("imageId")]
    public string? ImageId { get; set; }
}
