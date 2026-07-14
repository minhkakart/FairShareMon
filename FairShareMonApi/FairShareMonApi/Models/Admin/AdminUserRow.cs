namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Một dòng trong danh sách người dùng cho admin (M11 OQ7): chỉ metadata tài khoản + số liệu grant từ
/// bảng <c>tier_grants</c>. KHÔNG có bất kỳ số liệu sổ chi tiêu nào (R10).
/// </summary>
public class AdminUserRow
{
    public string Uuid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>Hạng người dùng (FREE/PREMIUM).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Vai trò (USER/ADMIN).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Trạng thái tài khoản (ACTIVE/DISABLED).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Số lần được cấp Premium (số dòng GRANT), lấy từ <c>tier_grants</c>.</summary>
    public int GrantCount { get; set; }

    /// <summary>Thời điểm cấp Premium gần nhất; null nếu chưa từng được cấp.</summary>
    public DateTime? LastGrantAt { get; set; }
}
