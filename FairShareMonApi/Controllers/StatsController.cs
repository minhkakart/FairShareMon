using FairShareMonApi.Models;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Services.Api.Stats;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Statistics endpoints (The-ideal.md §3.9): overview (tổng chi tiêu theo khoảng thời gian) and
/// by-category (thống kê theo danh mục theo khoảng thời gian hoặc theo một đợt). Read-only; all actions
/// require a valid access token and are resource-owned - another user's data never leaks, and an
/// event ownership miss yields 404 (never 403). Thin - all business logic in <see cref="IStatsService"/>.
/// </summary>
public class StatsController(IStatsService statsService) : AppController
{
    [HttpGet("overview")]
    [SwaggerOperation(
        Summary = "Thống kê tổng quan",
        Description = "Trả về tổng chi tiêu và số lượng phiếu của toàn bộ sổ (phiếu thuộc đợt lẫn phiếu rời) trong một khoảng thời gian. Hai mốc from/to đều tùy chọn (bỏ trống = toàn bộ thời gian), bao gồm cả hai đầu, so sánh theo UTC. from phải trước hoặc bằng to.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thống kê tổng quan thành công.", typeof(ApiResult<OverviewStatsResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Khoảng thời gian không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> GetOverviewAsync([FromQuery] StatsRangeRequest request, CancellationToken cancellationToken) =>
        ApiResult<OverviewStatsResponse>.Success(
            await statsService.GetOverviewAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpGet("by-category")]
    [SwaggerOperation(
        Summary = "Thống kê theo danh mục",
        Description = "Trả về tổng chi tiêu và số lượng phiếu theo từng danh mục (kèm màu/icon) để vẽ biểu đồ, sắp xếp theo tổng tiền giảm dần. Lọc theo khoảng thời gian HOẶC theo một đợt (eventUuid) - không dùng đồng thời. Chỉ hiện danh mục có ít nhất một phiếu; danh mục đã xóa mềm nhưng còn phiếu lịch sử vẫn xuất hiện.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thống kê theo danh mục thành công.", typeof(ApiResult<ByCategoryStatsResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Khoảng thời gian không hợp lệ hoặc dùng đồng thời đợt và khoảng thời gian.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> GetByCategoryAsync([FromQuery] ByCategoryStatsRequest request, CancellationToken cancellationToken) =>
        ApiResult<ByCategoryStatsResponse>.Success(
            await statsService.GetByCategoryAsync(AuthenticatedUser.Id, request, cancellationToken));
}
