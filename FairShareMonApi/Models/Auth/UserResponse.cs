namespace FairShareMonApi.Models.Auth;

/// <summary>Thông tin tài khoản trả về cho client (không bao giờ chứa mật khẩu hay hash).</summary>
public class UserResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
