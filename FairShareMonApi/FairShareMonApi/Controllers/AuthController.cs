using FairShareMonApi.Models;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Services.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Auth endpoints (The-ideal.md §3.1): register / login / refresh are anonymous; logout and
/// change-password require a valid access token (FallbackPolicy). Thin - all business logic in
/// <see cref="IAuthService"/>.
/// </summary>
public class AuthController(IAuthService authService, IStringLocalizer<StringResources> localizer) : AppController
{
    private const string BearerPrefix = "Bearer ";

    [AllowAnonymous]
    [HttpPost("register")]
    [SwaggerOperation(
        Summary = "Đăng ký tài khoản mới",
        Description = "Tạo tài khoản bằng tên đăng nhập và mật khẩu; hạng mặc định là FREE. Đăng ký xong cần gọi đăng nhập để nhận token.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đăng ký tài khoản thành công.", typeof(ApiResult<UserResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc tên đăng nhập đã tồn tại.", typeof(ApiResult))]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken cancellationToken) =>
        ApiResult<UserResponse>.Success(await authService.RegisterAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("login")]
    [SwaggerOperation(
        Summary = "Đăng nhập",
        Description = "Xác thực tên đăng nhập/mật khẩu và trả về cặp access + refresh token. Token chỉ được trả về đúng một lần - client phải tự lưu.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đăng nhập thành công.", typeof(ApiResult<TokenPairResponse>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Tên đăng nhập hoặc mật khẩu không đúng.", typeof(ApiResult))]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken) =>
        ApiResult<TokenPairResponse>.Success(await authService.LoginAsync(request, cancellationToken));

    // Deliberately returns the plain DTO: [ResponseWrapped] on AppController auto-wraps it into
    // the ApiResult envelope (first real exercise of the auto-wrapping handoff).
    [AllowAnonymous]
    [HttpPost("refresh")]
    [SwaggerOperation(
        Summary = "Gia hạn phiên đăng nhập",
        Description = "Đổi refresh token hợp lệ lấy cặp token mới (cặp cũ bị thu hồi ngay). Refresh token đã bị thu hồi mà dùng lại sẽ khiến mọi phiên của tài khoản bị đăng xuất.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Gia hạn phiên thành công.", typeof(ApiResult<TokenPairResponse>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Mã gia hạn phiên không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<TokenPairResponse> RefreshAsync([FromBody] RefreshRequest request, CancellationToken cancellationToken) =>
        await authService.RefreshAsync(request, cancellationToken);

    [HttpPost("logout")]
    [SwaggerOperation(
        Summary = "Đăng xuất",
        Description = "Thu hồi phiên hiện tại (cả access token và refresh token của cặp). Idempotent - token đã thu hồi vẫn trả về thành công.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đăng xuất thành công.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        // The service needs the RAW bearer token (it stores only hashes and must re-hash it).
        var authorization = Request.Headers.Authorization.ToString();
        var rawToken = authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[BearerPrefix.Length..].Trim()
            : string.Empty;

        await authService.LogoutAsync(rawToken, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.LoggedOut].Value);
    }

    [HttpGet("me")]
    [SwaggerOperation(
        Summary = "Thông tin tài khoản hiện tại",
        Description = "Trả về hồ sơ của người dùng đang đăng nhập (uuid, tên đăng nhập, hạng, vai trò).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin tài khoản thành công.", typeof(ApiResult<UserResponse>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        ApiResult<UserResponse>.Success(await authService.GetCurrentUserAsync(AuthenticatedUser.Id, cancellationToken));

    [HttpPost("change-password")]
    [SwaggerOperation(
        Summary = "Đổi mật khẩu",
        Description = "Xác nhận mật khẩu hiện tại, lưu mật khẩu mới và thu hồi TOÀN BỘ token của tài khoản - mọi thiết bị đang đăng nhập buộc phải đăng nhập lại.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đổi mật khẩu thành công.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc mật khẩu hiện tại không đúng.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await authService.ChangePasswordAsync(AuthenticatedUser.Id, request, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.PasswordChanged].Value);
    }
}
