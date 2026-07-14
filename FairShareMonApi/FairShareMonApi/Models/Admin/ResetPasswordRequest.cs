namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Yêu cầu đặt lại mật khẩu cho một người dùng (M11 OQ8). Admin cung cấp mật khẩu tạm; hệ thống băm +
/// lưu, thu hồi toàn bộ token của người dùng, và trả mật khẩu này về đúng một lần (không bao giờ ghi log).
/// </summary>
public class ResetPasswordRequest
{
    /// <summary>Mật khẩu tạm do admin đặt (áp dụng cùng quy tắc độ dài như đổi mật khẩu).</summary>
    public string NewPassword { get; set; } = string.Empty;
}
