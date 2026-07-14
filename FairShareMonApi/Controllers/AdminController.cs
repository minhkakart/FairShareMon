using FairShareMonApi.Constants;
using FairShareMonApi.Models;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Services.Api.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Admin management endpoints (The-ideal.md §3.11 + §4.1, M11): metrics/revenue dashboards and
/// account-level user administration (list/get/tier grant-revoke/disable-enable/revoke-tokens/
/// reset-password/role). Guarded by the <c>Admin</c> policy - non-admins (incl. valid Free/Premium
/// users) get 403 <c>Forbidden 1004</c>; anonymous -> 401. <b>No endpoint ever returns another user's
/// ledger data (members/expenses/events/shares/bank accounts) - admin acts only on account metadata +
/// tier-grant records (§4.1/R10).</b> Thin - all logic in <see cref="IAdminUserService"/> /
/// <see cref="IAdminDashboardService"/>.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Admin)]
public class AdminController(
    IAdminUserService adminUserService,
    IAdminDashboardService adminDashboardService) : AppController
{
    [HttpGet("dashboard")]
    [SwaggerOperation(
        Summary = "Bảng chỉ số quản trị",
        Description = "Trả về các con số dựa trên metadata tài khoản: tổng người dùng, phân bố theo hạng/vai trò/trạng thái, và số đăng ký theo thời gian. TUYỆT ĐỐI không có số liệu sổ chi tiêu (§4.1).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy bảng chỉ số thành công.", typeof(ApiResult<AdminMetricsResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Tham số không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    public async Task<IActionResult> GetDashboardAsync([FromQuery] AdminMetricsRequest request, CancellationToken cancellationToken) =>
        ApiResult<AdminMetricsResponse>.Success(await adminDashboardService.GetMetricsAsync(request, cancellationToken));

    [HttpGet("revenue")]
    [SwaggerOperation(
        Summary = "Bảng doanh thu",
        Description = "Tổng số tiền các lượt cấp Premium (dòng GRANT) trong khoảng thời gian, chia theo tháng (mặc định) hoặc ngày, kèm tổng chung và danh sách mã tham chiếu. Nguồn dữ liệu duy nhất là bảng tier_grants (§4.1).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy doanh thu thành công.", typeof(ApiResult<RevenueResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Tham số không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    public async Task<IActionResult> GetRevenueAsync([FromQuery] RevenueRequest request, CancellationToken cancellationToken) =>
        ApiResult<RevenueResponse>.Success(await adminDashboardService.GetRevenueAsync(request, cancellationToken));

    [HttpGet("users")]
    [SwaggerOperation(
        Summary = "Danh sách người dùng",
        Description = "Danh sách người dùng (chỉ metadata tài khoản + số liệu cấp Premium), có phân trang, lọc theo hạng/trạng thái/vai trò/tìm theo tên đăng nhập, và sắp xếp. Không có bất kỳ số liệu sổ chi tiêu nào (§4.1).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách người dùng thành công.", typeof(ApiResult<PagedResult<AdminUserRow>>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Tham số không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    public async Task<IActionResult> ListUsersAsync([FromQuery] AdminUserListRequest request, CancellationToken cancellationToken) =>
        ApiResult<PagedResult<AdminUserRow>>.Success(await adminUserService.ListAsync(request, cancellationToken));

    [HttpGet("users/{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết người dùng",
        Description = "Metadata tài khoản + lịch sử cấp/thu hồi hạng của một người dùng. Không có dữ liệu sổ chi tiêu (§4.1).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy chi tiết người dùng thành công.", typeof(ApiResult<AdminUserDetailResponse>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> GetUserAsync(string uuid, CancellationToken cancellationToken) =>
        ApiResult<AdminUserDetailResponse>.Success(await adminUserService.GetAsync(uuid, cancellationToken));

    [HttpPost("users/{uuid}/tier/grant")]
    [SwaggerOperation(
        Summary = "Cấp Premium thủ công",
        Description = "Nâng người dùng lên Premium và ghi lại số tiền thanh toán ngoại tuyến (kèm tham chiếu/ghi chú). Ghi một dòng GRANT vào tier_grants; hạng mới có hiệu lực ở lần gọi tiếp theo của người dùng, không buộc đăng xuất.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cấp Premium thành công.", typeof(ApiResult<TierGrantRow>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> GrantTierAsync(string uuid, [FromBody] GrantTierRequest request, CancellationToken cancellationToken) =>
        ApiResult<TierGrantRow>.Success(await adminUserService.GrantTierAsync(AuthenticatedUser, uuid, request, cancellationToken));

    [HttpPost("users/{uuid}/tier/revoke")]
    [SwaggerOperation(
        Summary = "Thu hồi Premium",
        Description = "Hạ người dùng về Free và ghi một dòng REVOKE (số tiền 0) vào tier_grants. Hạng mới có hiệu lực ở lần gọi tiếp theo, không buộc đăng xuất.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thu hồi Premium thành công.", typeof(ApiResult<TierGrantRow>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> RevokeTierAsync(string uuid, [FromBody] RevokeTierRequest request, CancellationToken cancellationToken) =>
        ApiResult<TierGrantRow>.Success(await adminUserService.RevokeTierAsync(AuthenticatedUser, uuid, request, cancellationToken));

    [HttpPost("users/{uuid}/disable")]
    [SwaggerOperation(
        Summary = "Vô hiệu hóa tài khoản",
        Description = "Đặt trạng thái tài khoản thành DISABLED, thu hồi ngay toàn bộ token và chặn đăng nhập cho tới khi được bật lại. Không thể vô hiệu hóa chính mình hoặc một admin khác.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Vô hiệu hóa tài khoản thành công.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không được phép thực hiện với tài khoản này.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> DisableUserAsync(string uuid, CancellationToken cancellationToken)
    {
        await adminUserService.DisableAsync(AuthenticatedUser, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã vô hiệu hóa tài khoản.");
    }

    [HttpPost("users/{uuid}/enable")]
    [SwaggerOperation(
        Summary = "Bật lại tài khoản",
        Description = "Đặt trạng thái tài khoản thành ACTIVE để cho phép đăng nhập trở lại (không tự khôi phục token cũ - người dùng đăng nhập lại).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Bật lại tài khoản thành công.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> EnableUserAsync(string uuid, CancellationToken cancellationToken)
    {
        await adminUserService.EnableAsync(AuthenticatedUser, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã bật lại tài khoản.");
    }

    [HttpPost("users/{uuid}/revoke-tokens")]
    [SwaggerOperation(
        Summary = "Thu hồi toàn bộ phiên",
        Description = "Thu hồi toàn bộ token của người dùng, buộc đăng nhập lại trên mọi thiết bị. Không thể thực hiện với chính mình hoặc một admin khác.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thu hồi phiên thành công.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không được phép thực hiện với tài khoản này.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> RevokeTokensAsync(string uuid, CancellationToken cancellationToken)
    {
        await adminUserService.RevokeTokensAsync(AuthenticatedUser, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã thu hồi toàn bộ phiên của người dùng.");
    }

    [HttpPost("users/{uuid}/reset-password")]
    [SwaggerOperation(
        Summary = "Đặt lại mật khẩu",
        Description = "Đặt mật khẩu tạm cho người dùng (băm + lưu), thu hồi toàn bộ token, và trả mật khẩu tạm về đúng MỘT lần để admin chuyển cho người dùng. Không thể thực hiện với chính mình hoặc một admin khác.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đặt lại mật khẩu thành công.", typeof(ApiResult<ResetPasswordResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc không được phép thực hiện với tài khoản này.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> ResetPasswordAsync(string uuid, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken) =>
        ApiResult<ResetPasswordResponse>.Success(await adminUserService.ResetPasswordAsync(AuthenticatedUser, uuid, request, cancellationToken));

    [HttpPost("users/{uuid}/role")]
    [SwaggerOperation(
        Summary = "Thăng/giáng vai trò",
        Description = "Đặt vai trò USER hoặc ADMIN cho người dùng. Vai trò mới có hiệu lực ở lần gọi tiếp theo, không buộc đăng xuất. Không thể tự giáng mình hay giáng một admin khác (hệ thống luôn còn ít nhất một admin).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật vai trò thành công.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc không được phép thực hiện với tài khoản này.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Không có quyền quản trị.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy người dùng.", typeof(ApiResult))]
    public async Task<IActionResult> SetRoleAsync(string uuid, [FromBody] SetRoleRequest request, CancellationToken cancellationToken)
    {
        await adminUserService.SetRoleAsync(AuthenticatedUser, uuid, request, cancellationToken);
        return ApiResult.SuccessMessage("Đã cập nhật vai trò người dùng.");
    }
}
