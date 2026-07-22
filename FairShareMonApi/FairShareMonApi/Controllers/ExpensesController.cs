using FairShareMonApi.Models;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Export;
using FairShareMonApi.Services.Api.Shares;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
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
public class ExpensesController(IExpensesService expensesService, ISharesService sharesService, IExportService exportService, IWalletQrService walletQrService, IStringLocalizer<StringResources> localizer) : AppController
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
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ, liên kết không hợp lệ, trùng thành viên phần gánh, hoặc tài khoản Free đã đạt giới hạn số phiếu chi tiêu trong tháng (nâng cấp Premium để bỏ giới hạn).", typeof(ApiResult))]
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
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.ExpenseDeleted].Value);
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
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.ExpenseSettledUpdated].Value);
    }

    [HttpPut("{uuid}/event")]
    [SwaggerOperation(
        Summary = "Gán phiếu vào đợt chi tiêu",
        Description = "Gán (hoặc chuyển) một phiếu chi tiêu vào một đợt. Đợt đích phải thuộc cùng tài khoản, đang mở và chứa thời điểm chi của phiếu. Nếu phiếu đang thuộc một đợt đã chốt thì không thể chuyển.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã gán phiếu vào đợt chi tiêu.", typeof(ApiResult<ExpenseResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Đợt đã chốt hoặc thời điểm chi không nằm trong khoảng thời gian của đợt.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu hoặc đợt chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> AssignEventAsync([FromRoute] string uuid, [FromBody] AssignEventRequest request, CancellationToken cancellationToken) =>
        ApiResult<ExpenseResponse>.Success(
            await expensesService.AssignEventAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpDelete("{uuid}/event")]
    [SwaggerOperation(
        Summary = "Gỡ phiếu khỏi đợt chi tiêu",
        Description = "Gỡ một phiếu chi tiêu khỏi đợt của nó (phiếu trở thành không thuộc đợt nào). Không làm gì nếu phiếu vốn không thuộc đợt nào. Không thể gỡ phiếu khỏi đợt đã chốt.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã gỡ phiếu khỏi đợt chi tiêu.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không thể gỡ phiếu khỏi đợt đã chốt.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> RemoveEventAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await expensesService.RemoveEventAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.ExpenseDetached].Value);
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
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.ShareDeleted].Value);
    }

    [HttpPut("{uuid}/shares/{shareUuid}/settled")]
    [SwaggerOperation(
        Summary = "Cập nhật trạng thái đã trả của phần gánh",
        Description = "Đánh dấu hoặc bỏ đánh dấu một phần gánh là đã trả (đã trả theo từng thành viên - §6). Đây là metadata thanh toán, không thay đổi số tiền và không ghi vào nhật ký thay đổi. Cho phép cả khi đợt đã chốt (ngoại lệ §4.4). Trạng thái đã trả của cả phiếu được đồng bộ lại theo các phần gánh.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã cập nhật trạng thái đã trả của phần gánh.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phần gánh hoặc phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> SetShareSettledAsync([FromRoute] string uuid, [FromRoute] string shareUuid, [FromBody] SetSettledRequest request, CancellationToken cancellationToken)
    {
        await sharesService.SetSettledAsync(AuthenticatedUser.Id, uuid, shareUuid, request, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.ShareSettledUpdated].Value);
    }

    [HttpGet("{uuid}/export")]
    [Produces("text/csv", "application/json")]
    [SwaggerOperation(
        Summary = "Xuất phiếu chi tiêu ra tệp",
        Description = "Xuất một phiếu chi tiêu của tài khoản ra tệp tải về (mặc định CSV): khối thông tin phiếu (tên, mô tả, thời điểm chi, người trả, danh mục, nhãn, đợt, đã trả, tổng tiền) kèm bảng phần gánh theo từng thành viên và dòng tổng cộng. Chọn định dạng bằng tham số format (mặc định csv); định dạng không hỗ trợ trả về 400. Chỉ đọc, resource-owned - phiếu không thuộc tài khoản trả về 404.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Xuất phiếu chi tiêu thành công (tệp CSV).", typeof(FileContentResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Định dạng xuất không được hỗ trợ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu.", typeof(ApiResult))]
    public async Task<IActionResult> ExportAsync([FromRoute] string uuid, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        var file = await exportService.ExportExpenseAsync(AuthenticatedUser.Id, uuid, format, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{uuid}/qr")]
    [Produces("image/png", "application/json")]
    [SwaggerOperation(
        Summary = "Tạo mã QR chuyển khoản cho phiếu",
        Description = "Tạo ảnh QR tổng hợp cho một phiếu chi tiêu: mỗi thành viên còn nợ trên phiếu (phần gánh chưa đánh dấu đã trả, không phải người trả) một mã VietQR với số tiền đúng bằng phần gánh của người đó, kèm nhãn tên + số tiền, gộp tất cả vào một ảnh PNG để chia sẻ vào nhóm chat. Đích nhận là tài khoản ngân hàng mặc định, hoặc tài khoản chỉ định qua tham số bankAccountUuid (phải thuộc tài khoản). Không còn ai nợ (tất cả đã trả) trả về 400. Chỉ đọc, resource-owned - phiếu không thuộc tài khoản trả về 404; chưa có tài khoản ngân hàng trả về 400.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Tạo mã QR tổng hợp thành công (ảnh PNG).", typeof(FileContentResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không còn ai nợ trên phiếu, hoặc chưa có tài khoản ngân hàng để tạo mã QR.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Tính năng tạo mã QR chỉ dành cho tài khoản Premium (nâng cấp để sử dụng).", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu hoặc tài khoản ngân hàng.", typeof(ApiResult))]
    public async Task<IActionResult> GetQrAsync([FromRoute] string uuid, [FromQuery] string? bankAccountUuid, CancellationToken cancellationToken)
    {
        var image = await walletQrService.GenerateExpenseQrAsync(AuthenticatedUser.Id, uuid, bankAccountUuid, cancellationToken);
        return File(image.Content, image.ContentType, image.FileName);
    }

    [HttpGet("{uuid}/qr/members")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Danh sách mã QR chuyển khoản theo từng thành viên còn nợ trên phiếu",
        Description = "Trả về danh sách mã QR chuyển khoản theo từng thành viên còn nợ trên phiếu chi tiêu (phần gánh chưa đánh dấu đã trả, khác 0đ, không phải người trả): mỗi thành viên một ảnh QR VietQR riêng dạng data URL (data:image/png;base64,...) với số tiền đúng bằng phần gánh của người đó, kèm tên thành viên và số tiền trong phần đầu ảnh. Thứ tự theo thứ tự phần gánh của phiếu. Đích nhận là tài khoản ngân hàng mặc định, hoặc tài khoản chỉ định qua tham số bankAccountUuid (phải thuộc tài khoản). Không còn ai nợ (tất cả đã trả) trả về 400. Chỉ đọc, resource-owned - phiếu không thuộc tài khoản trả về 404; chưa có tài khoản ngân hàng trả về 400.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Tạo danh sách mã QR theo từng thành viên thành công.", typeof(ApiResult<IReadOnlyList<MemberQrResponse>>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Không còn ai nợ trên phiếu, hoặc chưa có tài khoản ngân hàng để tạo mã QR.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Tính năng tạo mã QR chỉ dành cho tài khoản Premium (nâng cấp để sử dụng).", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy phiếu chi tiêu hoặc tài khoản ngân hàng.", typeof(ApiResult))]
    public async Task<IActionResult> GetMemberQrsAsync([FromRoute] string uuid, [FromQuery] string? bankAccountUuid, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<MemberQrResponse>>.Success(
            await walletQrService.GenerateExpenseMemberQrsAsync(AuthenticatedUser.Id, uuid, bankAccountUuid, cancellationToken));

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
