using System.Text.Json.Serialization;

namespace FairShareMonApi.Models.Banks;

/// <summary>
/// Thân yêu cầu gửi tới VietQR để tạo nội dung QR (POST <c>/api/vietqr/generate</c>). DTO nội bộ; không có
/// header xác thực/ngôn ngữ nào của ứng dụng được gửi kèm tới bên thứ ba.
/// </summary>
public class VietQrGenerateRequest
{
    /// <summary>Mã ngân hàng đã phân giải từ danh mục theo BIN.</summary>
    [JsonPropertyName("bankCode")]
    public string? BankCode { get; set; }

    /// <summary>Mã BIN của ngân hàng (một số API dùng trường này).</summary>
    [JsonPropertyName("acqId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AcqId { get; set; }

    /// <summary>Số tài khoản nhận.</summary>
    [JsonPropertyName("accountNo")]
    public string? AccountNo { get; set; }

    /// <summary>Tên chủ tài khoản nhận.</summary>
    [JsonPropertyName("accountName")]
    public string? AccountName { get; set; }

    /// <summary>Số tiền chuyển (VND).</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>Nội dung chuyển khoản.</summary>
    [JsonPropertyName("addInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddInfo { get; set; }

    /// <summary>Định dạng phản hồi mong muốn (chuỗi QR).</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "text";

    /// <summary>Mẫu QR.</summary>
    [JsonPropertyName("template")]
    public string Template { get; set; } = "compact";
}
