using FairShareMonApi.Models;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Stats;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Event endpoints (The-ideal.md §3.6): list (with an optional closed filter) / get / create / update
/// info / delete / close the current user's spending events. All actions require a valid access token
/// and are resource-owned - an event that isn't the caller's yields 404 (never 403). Close is one-way;
/// edit/delete are OPEN-only; a closed event rejects every write to its expenses/shares except the
/// settled flag (§4.4). Thin - all business logic in <see cref="IEventsService"/>.
/// </summary>
public class EventsController(IEventsService eventsService, IStatsService statsService) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách đợt chi tiêu",
        Description = "Trả về các đợt chi tiêu của tài khoản, sắp xếp theo ngày bắt đầu giảm dần rồi thời điểm tạo giảm dần. Lọc theo trạng thái đã chốt bằng tham số closed (tùy chọn).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách đợt chi tiêu thành công.", typeof(ApiResult<IReadOnlyList<EventSummaryResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync([FromQuery] EventFilter filter, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<EventSummaryResponse>>.Success(
            await eventsService.ListAsync(AuthenticatedUser.Id, filter, cancellationToken));

    [HttpGet("{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết một đợt chi tiêu",
        Description = "Trả về thông tin một đợt chi tiêu (kèm số lượng phiếu suy ra) theo UUID. Không nhúng danh sách phiếu - dùng GET /expenses?eventUuid=… để lấy các phiếu của đợt.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin đợt chi tiêu thành công.", typeof(ApiResult<EventResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> GetAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<EventResponse>.Success(
            await eventsService.GetAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPost]
    [SwaggerOperation(
        Summary = "Thêm đợt chi tiêu",
        Description = "Tạo một đợt chi tiêu mới (tên, mô tả tùy chọn, khoảng thời gian trọn ngày). Ngày kết thúc phải sau hoặc bằng ngày bắt đầu. Đợt mới luôn ở trạng thái đang mở.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm đợt chi tiêu thành công.", typeof(ApiResult<EventResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", typeof(ApiResult))]
    public async Task<IActionResult> CreateAsync([FromBody] CreateEventRequest request, CancellationToken cancellationToken) =>
        ApiResult<EventResponse>.Success(
            await eventsService.CreateAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpPut("{uuid}")]
    [SwaggerOperation(
        Summary = "Cập nhật thông tin đợt chi tiêu",
        Description = "Cập nhật tên, mô tả và khoảng thời gian của một đợt đang mở. Không thể sửa đợt đã chốt. Không thể đổi khoảng thời gian nếu có phiếu đã gán nằm ngoài khoảng thời gian mới.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật đợt chi tiêu thành công.", typeof(ApiResult<EventResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ, đợt đã chốt hoặc khoảng thời gian mới loại phiếu đã gán ra ngoài.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> UpdateAsync([FromRoute] string uuid, [FromBody] UpdateEventRequest request, CancellationToken cancellationToken) =>
        ApiResult<EventResponse>.Success(
            await eventsService.UpdateAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpDelete("{uuid}")]
    [SwaggerOperation(
        Summary = "Xóa đợt chi tiêu",
        Description = "Xóa cứng một đợt đang mở. Các phiếu thuộc đợt không bị xóa mà trở thành phiếu không thuộc đợt nào. Không thể xóa đợt đã chốt.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa đợt chi tiêu.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không thể xóa đợt đã chốt.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await eventsService.DeleteAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã xóa đợt chi tiêu.");
    }

    [HttpGet("{uuid}/balance")]
    [SwaggerOperation(
        Summary = "Cân bằng nợ của đợt chi tiêu",
        Description = "Trả về cân bằng nợ của một đợt: với mỗi thành viên tham gia (người trả phiếu hoặc người gánh, kể cả thành viên đại diện ở mức 0đ và thành viên đã xóa mềm) là số tiền đã ứng, phải gánh và cân bằng (= đã ứng - phải gánh). Tổng cân bằng của tất cả thành viên luôn bằng 0. Xem được cho cả đợt đang mở và đã chốt; bỏ qua trạng thái đã trả. Đợt chưa có phiếu -> danh sách rỗng.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy cân bằng nợ thành công.", typeof(ApiResult<EventBalanceResponse>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> GetBalanceAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<EventBalanceResponse>.Success(
            await statsService.GetEventBalanceAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPut("{uuid}/close")]
    [SwaggerOperation(
        Summary = "Chốt đợt chi tiêu",
        Description = "Chốt một đợt chi tiêu. Hành động này một chiều (không mở lại được): sau khi chốt, mọi thay đổi đối với phiếu/phần gánh của đợt đều bị từ chối, trừ trạng thái đã trả. Chốt lại một đợt đã chốt sẽ bị từ chối.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã chốt đợt chi tiêu.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Đợt chi tiêu đã chốt.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> CloseAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await eventsService.CloseAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã chốt đợt chi tiêu.");
    }
}
