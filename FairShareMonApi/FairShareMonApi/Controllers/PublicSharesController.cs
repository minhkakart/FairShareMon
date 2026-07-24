using FairShareMonApi.Models;
using FairShareMonApi.Models.Share;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Services.Api.Share;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Anonymous public read of a shared CLOSED event (planning/event-share-link.md). No token auth, no
/// account: anyone with the opaque share link opens a LIVE read-only report (per-member balance,
/// per-expense breakdown) and per-member VietQR images. Derives <see cref="AppController"/> for the
/// versioned <c>[ResponseWrapped]</c> envelope but overrides the route to <c>public/shares</c> and is
/// <c>[AllowAnonymous]</c> (the derived route attribute wins over the base). It <b>never</b> reads
/// <c>AuthenticatedUser</c> - owner + event are resolved from the token; the view is never re-gated
/// (§4 rule 9). An unknown / expired / revoked token yields 404 <c>ShareLinkNotFoundOrExpired</c> (16000).
/// </summary>
[AllowAnonymous]
[Route("api/v{version:apiVersion}/public/shares")]
public class PublicSharesController(IEventShareService shareService) : AppController
{
    [HttpGet("{token}")]
    [SwaggerOperation(
        Summary = "Xem báo cáo chia sẻ công khai của đợt (chỉ xem, không cần đăng nhập)",
        Description = "Trả về báo cáo chỉ-xem của một đợt đã chốt theo token chia sẻ: tên đợt, thời điểm chốt, cân bằng theo từng thành viên, chi tiết phần gánh từng phiếu, tổng còn nợ/số người còn nợ/số người đã trả, và cờ hasQr. Đây là bản đọc trực tiếp: số liệu chi tiêu cố định nhưng trạng thái đã trả/còn nợ phản ánh hiện tại. Không cần token đăng nhập. Token không tồn tại/đã hết hạn/đã thu hồi trả về 404.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy báo cáo chia sẻ thành công.", typeof(ApiResult<PublicEventShareResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Liên kết chia sẻ không tồn tại hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> GetPublicAsync([FromRoute] string token, CancellationToken cancellationToken) =>
        ApiResult<PublicEventShareResponse>.Success(
            await shareService.GetPublicAsync(token, cancellationToken));

    [HttpGet("{token}/qr/members")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Mã QR chuyển khoản theo từng thành viên còn nợ (chia sẻ công khai, không cần đăng nhập)",
        Description = "Trả về danh sách mã QR VietQR theo từng thành viên còn nợ trong đợt được chia sẻ, mỗi thành viên một ảnh QR dạng data URL (data:image/png;base64,...) với số tiền đúng bằng khoản nợ. Đích nhận là ảnh chụp ngân hàng lưu trên liên kết. Danh sách rỗng khi không còn ai nợ hoặc liên kết không kèm QR (hasQr=false). Không cần token đăng nhập. Token không tồn tại/đã hết hạn/đã thu hồi trả về 404.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách mã QR theo thành viên thành công (có thể rỗng).", typeof(ApiResult<IReadOnlyList<MemberQrResponse>>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Liên kết chia sẻ không tồn tại hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> GetPublicMemberQrsAsync([FromRoute] string token, CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<MemberQrResponse>>.Success(
            await shareService.GetPublicMemberQrsAsync(token, cancellationToken));
}
