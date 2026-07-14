namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Một dòng lịch sử cấp/thu hồi hạng (M11): ảnh chụp từ <c>tier_grants</c>. Hiển thị bằng tên đăng nhập
/// đã lưu sẵn (denormalized) nên không cần join lại vào <c>users</c> (an toàn về quyền riêng tư).
/// </summary>
public class TierGrantRow
{
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Hạng sau thao tác (PREMIUM khi cấp, FREE khi thu hồi).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Loại thao tác (GRANT/REVOKE).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Số tiền thanh toán ngoại tuyến (0 với thu hồi hoặc cấp miễn phí).</summary>
    public decimal Amount { get; set; }

    /// <summary>Đơn vị tiền tệ (mặc định VND).</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Mã tham chiếu thanh toán (tùy chọn).</summary>
    public string? Reference { get; set; }

    /// <summary>Ghi chú của admin (tùy chọn).</summary>
    public string? Note { get; set; }

    /// <summary>Tên đăng nhập admin thực hiện thao tác.</summary>
    public string GrantedByUsername { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
