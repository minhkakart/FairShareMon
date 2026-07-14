namespace FairShareMonApi.Models.Auth;

/// <summary>Cặp token trả về đúng một lần sau đăng nhập/gia hạn - client phải tự lưu lại.</summary>
public class TokenPairResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; set; }

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime RefreshTokenExpiresAt { get; set; }
}
