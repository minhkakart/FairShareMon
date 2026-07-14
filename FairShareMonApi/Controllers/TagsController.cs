using FairShareMonApi.Models;
using FairShareMonApi.Models.Tags;
using FairShareMonApi.Services.Api.Tags;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Tag endpoints (The-ideal.md §3.4): list / get / create / rename / soft-delete the current user's
/// tags. All actions require a valid access token and are resource-owned - a tag that isn't the
/// caller's yields 404 (never 403). Thin - all business logic in <see cref="ITagsService"/>.
/// </summary>
public class TagsController(ITagsService tagsService) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách nhãn",
        Description = "Trả về các nhãn của tài khoản, sắp xếp theo tên A→Z. Đặt includeDeleted=true để lấy cả nhãn đã xóa (dùng cho thống kê/xuất dữ liệu).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách nhãn thành công.", typeof(ApiResult<IReadOnlyList<TagResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync([FromQuery] bool includeDeleted, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<TagResponse>>.Success(
            await tagsService.ListAsync(AuthenticatedUser.Id, includeDeleted, cancellationToken));

    [HttpGet("{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết một nhãn",
        Description = "Trả về thông tin một nhãn của tài khoản theo UUID.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin nhãn thành công.", typeof(ApiResult<TagResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy nhãn.", typeof(ApiResult))]
    public async Task<IActionResult> GetAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<TagResponse>.Success(
            await tagsService.GetAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPost]
    [SwaggerOperation(
        Summary = "Thêm nhãn",
        Description = "Tạo một nhãn mới. Nếu tên trùng với một nhãn đã xóa thì nhãn đó sẽ được kích hoạt lại (giữ nguyên lịch sử) thay vì tạo trùng.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm nhãn thành công.", typeof(ApiResult<TagResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc tên nhãn đã tồn tại.", typeof(ApiResult))]
    public async Task<IActionResult> CreateAsync([FromBody] CreateTagRequest request, CancellationToken cancellationToken) =>
        ApiResult<TagResponse>.Success(
            await tagsService.CreateAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpPut("{uuid}")]
    [SwaggerOperation(
        Summary = "Đổi tên nhãn",
        Description = "Đổi tên một nhãn của tài khoản.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật nhãn thành công.", typeof(ApiResult<TagResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc tên nhãn đã tồn tại.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy nhãn.", typeof(ApiResult))]
    public async Task<IActionResult> RenameAsync([FromRoute] string uuid, [FromBody] UpdateTagRequest request, CancellationToken cancellationToken) =>
        ApiResult<TagResponse>.Success(
            await tagsService.RenameAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpDelete("{uuid}")]
    [SwaggerOperation(
        Summary = "Xóa nhãn",
        Description = "Xóa mềm một nhãn: nhãn biến mất khỏi các danh sách chọn khi tạo dữ liệu mới, nhưng dữ liệu lịch sử vẫn giữ nguyên.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa nhãn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy nhãn.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await tagsService.DeleteAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã xóa nhãn.");
    }
}
