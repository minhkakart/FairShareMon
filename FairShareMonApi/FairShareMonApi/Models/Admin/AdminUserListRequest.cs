namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Tham số lọc/phân trang/sắp xếp cho danh sách người dùng (chỉ dữ liệu tài khoản, M11 OQ7). Mọi bộ lọc
/// đều tùy chọn. Chỉ trả về thông tin tài khoản - KHÔNG bao giờ có dữ liệu sổ chi tiêu (R10).
/// </summary>
public class AdminUserListRequest
{
    /// <summary>Lọc theo hạng (FREE/PREMIUM), tùy chọn.</summary>
    public string? Tier { get; set; }

    /// <summary>Lọc theo trạng thái (ACTIVE/DISABLED), tùy chọn.</summary>
    public string? Status { get; set; }

    /// <summary>Lọc theo vai trò (USER/ADMIN), tùy chọn.</summary>
    public string? Role { get; set; }

    /// <summary>Tìm theo tên đăng nhập (chứa chuỗi), tùy chọn.</summary>
    public string? Search { get; set; }

    /// <summary>Số trang (bắt đầu từ 1); mặc định 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Kích thước trang; mặc định 20, tối đa 100.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Trường sắp xếp: createdAt (mặc định) | username | tier | status.</summary>
    public string Sort { get; set; } = "createdAt";

    /// <summary>Chiều sắp xếp: desc (mặc định) | asc.</summary>
    public string Direction { get; set; } = "desc";
}
