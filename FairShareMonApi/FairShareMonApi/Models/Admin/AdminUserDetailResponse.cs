namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Chi tiết một người dùng cho admin (M11): metadata tài khoản + lịch sử cấp/thu hồi hạng. KHÔNG có dữ
/// liệu sổ chi tiêu (thành viên/phiếu/đợt/phần gánh/tài khoản ngân hàng) - R10.
/// </summary>
public class AdminUserDetailResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Lịch sử cấp/thu hồi hạng (mới nhất trước).</summary>
    public IReadOnlyList<TierGrantRow> Grants { get; set; } = [];
}
