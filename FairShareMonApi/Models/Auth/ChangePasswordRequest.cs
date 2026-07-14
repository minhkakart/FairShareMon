namespace FairShareMonApi.Models.Auth;

/// <summary>Yêu cầu đổi mật khẩu (bắt buộc kèm mật khẩu hiện tại).</summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}
