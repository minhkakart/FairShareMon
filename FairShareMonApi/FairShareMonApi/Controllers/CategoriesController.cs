using FairShareMonApi.Models;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Services.Api.Categories;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Category endpoints (The-ideal.md §3.3): list / get / create / update / soft-delete the current
/// user's expense categories, plus set the default. All actions require a valid access token and are
/// resource-owned - a category that isn't the caller's yields 404 (never 403). Thin - all business
/// logic in <see cref="ICategoriesService"/>.
/// </summary>
public class CategoriesController(ICategoriesService categoriesService, IStringLocalizer<StringResources> localizer) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách danh mục",
        Description = "Trả về các danh mục chi tiêu của tài khoản, sắp xếp danh mục mặc định trước rồi đến tên A→Z. Đặt includeDeleted=true để lấy cả danh mục đã xóa (dùng cho thống kê/xuất dữ liệu).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách danh mục thành công.", typeof(ApiResult<IReadOnlyList<CategoryResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync([FromQuery] bool includeDeleted, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<CategoryResponse>>.Success(
            await categoriesService.ListAsync(AuthenticatedUser.Id, includeDeleted, cancellationToken));

    [HttpGet("{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết một danh mục",
        Description = "Trả về thông tin một danh mục chi tiêu của tài khoản theo UUID.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin danh mục thành công.", typeof(ApiResult<CategoryResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy danh mục.", typeof(ApiResult))]
    public async Task<IActionResult> GetAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<CategoryResponse>.Success(
            await categoriesService.GetAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPost]
    [SwaggerOperation(
        Summary = "Thêm danh mục",
        Description = "Tạo một danh mục chi tiêu mới (tên, màu, icon). Nếu tên trùng với một danh mục đã xóa thì danh mục đó sẽ được kích hoạt lại thay vì tạo trùng.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm danh mục thành công.", typeof(ApiResult<CategoryResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc tên danh mục đã tồn tại.", typeof(ApiResult))]
    public async Task<IActionResult> CreateAsync([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken) =>
        ApiResult<CategoryResponse>.Success(
            await categoriesService.CreateAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpPut("{uuid}")]
    [SwaggerOperation(
        Summary = "Cập nhật danh mục",
        Description = "Cập nhật tên, màu và icon của một danh mục. Không thể đổi cờ mặc định qua endpoint này (dùng đặt danh mục mặc định).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật danh mục thành công.", typeof(ApiResult<CategoryResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc tên danh mục đã tồn tại.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy danh mục.", typeof(ApiResult))]
    public async Task<IActionResult> UpdateAsync([FromRoute] string uuid, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        ApiResult<CategoryResponse>.Success(
            await categoriesService.UpdateAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpPut("{uuid}/default")]
    [SwaggerOperation(
        Summary = "Đặt danh mục mặc định",
        Description = "Đặt một danh mục làm mặc định: cờ mặc định của danh mục cũ được gỡ và gán cho danh mục này trong cùng một giao dịch. Chỉ áp dụng với danh mục đang hoạt động.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã đặt danh mục mặc định.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy danh mục.", typeof(ApiResult))]
    public async Task<IActionResult> SetDefaultAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await categoriesService.SetDefaultAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.CategorySetDefault].Value);
    }

    [HttpDelete("{uuid}")]
    [SwaggerOperation(
        Summary = "Xóa danh mục",
        Description = "Xóa mềm một danh mục: danh mục biến mất khỏi các danh sách chọn khi tạo dữ liệu mới, nhưng dữ liệu lịch sử vẫn giữ nguyên. Không thể xóa danh mục mặc định.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa danh mục.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không thể xóa danh mục mặc định.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy danh mục.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await categoriesService.DeleteAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.CategoryDeleted].Value);
    }
}
