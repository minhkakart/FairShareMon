namespace FairShareMonApi.Models.Auth;

/// <summary>Thông tin tài khoản trả về cho client (không bao giờ chứa mật khẩu hay hash).</summary>
public class UserResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    /// <summary>Vai trò của người dùng (USER/ADMIN) — client dùng để mở khoá giao diện quản trị.</summary>
    public string Role { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
