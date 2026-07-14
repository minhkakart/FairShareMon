namespace FairShareMonApi.Models.Auth;

/// <summary>Yêu cầu đăng ký tài khoản mới.</summary>
public class RegisterRequest
{
    /// <summary>Tên đăng nhập (3-32 ký tự, chỉ gồm a-z 0-9 _ . - , lưu ở dạng chữ thường).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Mật khẩu (tối thiểu 8 ký tự, tối đa 72 byte).</summary>
    public string Password { get; set; } = string.Empty;
}
