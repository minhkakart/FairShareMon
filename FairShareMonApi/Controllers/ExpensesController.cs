using FairShareMonApi.Models;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Shares;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Expense endpoints (The-ideal.md §3.5, §3.8): list (with filters) / get / create (atomic with
/// shares) / update general info / delete the current user's expenses, the settled toggle, the share
/// sub-routes, and the per-expense change history. All actions require a valid access token and are
/// resource-owned - an expense/share that isn't the caller's yields 404 (never 403). Thin - all
/// business logic in <see cref="IExpensesService"/> / <see cref="ISharesService"/>.
/// </summary>
public class ExpensesController(IExpensesService expensesService, ISharesService sharesService) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách phiếu chi tiêu",
        Description = "Trả về các phiếu chi tiêu của tài khoản, sắp xếp theo thời điểm chi giảm dần. Lọc theo khoảng thời gian (from/to, bao gồm hai đầu), danh mục, nhãn và trạng thái đã trả (kết hợp AND).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách phiếu chi tiêu thành công.", typeof(ApiResult<IReadOnlyList<ExpenseSummaryResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync([FromQuery] ExpenseFilter filter, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<ExpenseSummaryResponse>>.Success(
            await expensesService.ListAsync(AuthenticatedUser.Id, filter, cancellationToken));

    [HttpGet("{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết một phiếu chi tiêu",
        Description = "Trả về thông tin đầy đủ một phiếu chi tiêu (gồm phần gánh, nhãn, danh mục, người trả và tổng tiền suy ra) theo UUID.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin phiếu chi tiêu thành công.", typeof(ApiResult<ExpenseResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> GetAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<ExpenseResponse>.Success(
            await expensesService.GetAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPost]
    [SwaggerOperation(
        Summary = "Thêm phiếu chi tiêu",
        Description = "Tạo một phiếu chi tiêu mới cùng các phần gánh trong một giao dịch. Bỏ trống người trả để mặc định là thành viên đại diện chủ sổ; bỏ trống danh mục để dùng danh mục mặc định; phần gánh 0đ của thành viên đại diện chủ sổ được tự thêm nếu thiếu.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm phiếu chi tiêu thành công.", typeof(ApiResult<ExpenseResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ, liên kết không hợp lệ hoặc trùng thành viên phần gánh.", typeof(ApiResult))]
    public async Task<IActionResult> CreateAsync([FromBody] CreateExpenseRequest request, CancellationToken cancellationToken) =>
        ApiResult<ExpenseResponse>.Success(
            await expensesService.CreateAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpPut("{uuid}")]
    [SwaggerOperation(
        Summary = "Cập nhật thông tin phiếu chi tiêu",
        Description = "Cập nhật thông tin chung của phiếu (tên, mô tả, thời điểm chi, người trả, danh mục và tập nhãn - thay thế toàn bộ). Không sửa phần gánh qua endpoint này.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật phiếu chi tiêu thành công.", typeof(ApiResult<ExpenseResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ hoặc liên kết không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> UpdateAsync([FromRoute] string uuid, [FromBody] UpdateExpenseRequest request, CancellationToken cancellationToken) =>
        ApiResult<ExpenseResponse>.Success(
            await expensesService.UpdateAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpDelete("{uuid}")]
    [SwaggerOperation(
        Summary = "Xóa phiếu chi tiêu",
        Description = "Xóa cứng một phiếu chi tiêu và toàn bộ phần gánh của nó (cùng liên kết nhãn) trong một giao dịch. Nhật ký thay đổi vẫn được giữ lại.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa phiếu chi tiêu.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await expensesService.DeleteAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã xóa phiếu chi tiêu.");
    }

    [HttpPut("{uuid}/settled")]
    [SwaggerOperation(
        Summary = "Cập nhật trạng thái đã trả",
        Description = "Đánh dấu hoặc bỏ đánh dấu phiếu chi tiêu là đã trả. Đây là metadata thanh toán, không thay đổi số tiền và không ghi vào nhật ký thay đổi.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã cập nhật trạng thái đã trả.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> SetSettledAsync([FromRoute] string uuid, [FromBody] SetSettledRequest request, CancellationToken cancellationToken)
    {
        await expensesService.SetSettledAsync(AuthenticatedUser.Id, uuid, request, cancellationToken);
        return ApiResult.SuccessMessage("Đã cập nhật trạng thái đã trả.");
    }

    [HttpPost("{uuid}/shares")]
    [SwaggerOperation(
        Summary = "Thêm phần gánh",
        Description = "Thêm một phần gánh vào phiếu chi tiêu. Thành viên phải thuộc cùng tài khoản và chưa bị xóa; mỗi thành viên chỉ có một phần gánh trong một phiếu.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm phần gánh thành công.", typeof(ApiResult<ShareResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ, thành viên không hợp lệ hoặc trùng thành viên phần gánh.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> AddShareAsync([FromRoute] string uuid, [FromBody] CreateShareRequest request, CancellationToken cancellationToken) =>
        ApiResult<ShareResponse>.Success(
            await sharesService.AddAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpPut("{uuid}/shares/{shareUuid}")]
    [SwaggerOperation(
        Summary = "Cập nhật phần gánh",
        Description = "Cập nhật số tiền, ghi chú hoặc đổi thành viên của một phần gánh. Không thể đổi thành viên của phần gánh thuộc thành viên đại diện chủ sổ.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật phần gánh thành công.", typeof(ApiResult<ShareResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ, thành viên không hợp lệ hoặc trùng thành viên phần gánh.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phần gánh.", typeof(ApiResult))]
    public async Task<IActionResult> UpdateShareAsync([FromRoute] string uuid, [FromRoute] string shareUuid, [FromBody] UpdateShareRequest request, CancellationToken cancellationToken) =>
        ApiResult<ShareResponse>.Success(
            await sharesService.UpdateAsync(AuthenticatedUser.Id, uuid, shareUuid, request, cancellationToken));

    [HttpDelete("{uuid}/shares/{shareUuid}")]
    [SwaggerOperation(
        Summary = "Xóa phần gánh",
        Description = "Xóa một phần gánh khỏi phiếu chi tiêu. Không thể xóa phần gánh của thành viên đại diện chủ sổ.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa phần gánh.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không thể xóa phần gánh của thành viên đại diện chủ sổ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phần gánh.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteShareAsync([FromRoute] string uuid, [FromRoute] string shareUuid, CancellationToken cancellationToken)
    {
        await sharesService.DeleteAsync(AuthenticatedUser.Id, uuid, shareUuid, cancellationToken);
        return ApiResult.SuccessMessage("Đã xóa phần gánh.");
    }

    [HttpGet("{uuid}/history")]
    [SwaggerOperation(
        Summary = "Nhật ký thay đổi của phiếu",
        Description = "Trả về nhật ký thay đổi (tạo/sửa/xóa phiếu và phần gánh) của phiếu chi tiêu, sắp xếp theo thời gian tăng dần. Vẫn xem được kể cả khi phiếu đã bị xóa; UUID không thuộc tài khoản trả về danh sách rỗng.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy nhật ký thay đổi thành công.", typeof(ApiResult<IReadOnlyList<AuditLogResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> GetHistoryAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<AuditLogResponse>>.Success(
            await expensesService.GetHistoryAsync(AuthenticatedUser.Id, uuid, cancellationToken));
}
