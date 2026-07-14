namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Yêu cầu cấp Premium thủ công cho một người dùng (M11 OQ4/OQ15). <c>Amount</c> bắt buộc, <c>&gt;= 0</c>
/// (0 = cấp miễn phí); <c>Currency</c> tùy chọn (mặc định VND); <c>Reference</c>/<c>Note</c> tùy chọn.
/// </summary>
public class GrantTierRequest
{
    /// <summary>Số tiền thanh toán ngoại tuyến (>= 0; 0 cho cấp miễn phí).</summary>
    public decimal Amount { get; set; }

    /// <summary>Đơn vị tiền tệ (tùy chọn, mặc định VND).</summary>
    public string? Currency { get; set; }

    /// <summary>Mã tham chiếu thanh toán (tùy chọn).</summary>
    public string? Reference { get; set; }

    /// <summary>Ghi chú của admin (tùy chọn).</summary>
    public string? Note { get; set; }
}
