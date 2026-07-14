namespace FairShareMonApi.Models.Auth;

/// <summary>Yêu cầu gia hạn phiên bằng refresh token.</summary>
public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
