namespace FairShareMonApi.Models.Auth;

/// <summary>Yêu cầu đăng nhập.</summary>
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
