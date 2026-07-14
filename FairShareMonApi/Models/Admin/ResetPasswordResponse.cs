namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Kết quả đặt lại mật khẩu (M11 OQ8): trả về mật khẩu tạm đúng MỘT lần để admin chuyển cho người dùng
/// qua kênh ngoài. Giá trị này không bao giờ được ghi log.
/// </summary>
public class ResetPasswordResponse
{
    public string Username { get; set; } = string.Empty;

    /// <summary>Mật khẩu tạm (trả về một lần duy nhất).</summary>
    public string Password { get; set; } = string.Empty;
}
