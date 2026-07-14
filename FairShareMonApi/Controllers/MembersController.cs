using FairShareMonApi.Models;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Services.Api.Members;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Member endpoints (The-ideal.md §3.2): list / get / create / rename / soft-delete the current
/// user's ledger members. All actions require a valid access token and are resource-owned - a
/// member that isn't the caller's yields 404 (never 403). Thin - all business logic in
/// <see cref="IMembersService"/>.
/// </summary>
public class MembersController(IMembersService membersService) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách thành viên",
        Description = "Trả về các thành viên của tài khoản, sắp xếp thành viên đại diện chủ sổ trước rồi đến tên A→Z. Đặt includeDeleted=true để lấy cả thành viên đã xóa (dùng cho thống kê/xuất dữ liệu).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách thành viên thành công.", typeof(ApiResult<IReadOnlyList<MemberResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync([FromQuery] bool includeDeleted, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<MemberResponse>>.Success(
            await membersService.ListAsync(AuthenticatedUser.Id, includeDeleted, cancellationToken));

    [HttpGet("{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết một thành viên",
        Description = "Trả về thông tin một thành viên của tài khoản theo UUID.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin thành viên thành công.", typeof(ApiResult<MemberResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy thành viên.", typeof(ApiResult))]
    public async Task<IActionResult> GetAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<MemberResponse>.Success(
            await membersService.GetAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPost]
    [SwaggerOperation(
        Summary = "Thêm thành viên",
        Description = "Tạo một thành viên mới cho tài khoản. Tên được phép trùng với thành viên khác.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm thành viên thành công.", typeof(ApiResult<MemberResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc tài khoản Free đã đạt giới hạn số thành viên (nâng cấp Premium để bỏ giới hạn).", typeof(ApiResult))]
    public async Task<IActionResult> CreateAsync([FromBody] CreateMemberRequest request, CancellationToken cancellationToken) =>
        ApiResult<MemberResponse>.Success(
            await membersService.CreateAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpPut("{uuid}")]
    [SwaggerOperation(
        Summary = "Đổi tên thành viên",
        Description = "Đổi tên hiển thị của một thành viên. Cho phép đổi tên cả thành viên đại diện chủ sổ.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật thành viên thành công.", typeof(ApiResult<MemberResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy thành viên.", typeof(ApiResult))]
    public async Task<IActionResult> RenameAsync([FromRoute] string uuid, [FromBody] UpdateMemberRequest request, CancellationToken cancellationToken) =>
        ApiResult<MemberResponse>.Success(
            await membersService.RenameAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpDelete("{uuid}")]
    [SwaggerOperation(
        Summary = "Xóa thành viên",
        Description = "Xóa mềm một thành viên: thành viên biến mất khỏi các danh sách chọn khi tạo dữ liệu mới, nhưng dữ liệu lịch sử vẫn giữ nguyên. Không thể xóa thành viên đại diện chủ sổ.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa thành viên.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không thể xóa thành viên đại diện chủ sổ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy thành viên.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await membersService.DeleteAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã xóa thành viên.");
    }
}
