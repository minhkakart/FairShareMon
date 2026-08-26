using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
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
/// Also exposes the live-update SSE stream (planning/public-share-sse-updates.md).
/// </summary>
[AllowAnonymous]
[Route("api/v{version:apiVersion}/public/shares")]
public class PublicSharesController(
    IEventShareService shareService,
    IEventShareLinkCache shareLinkCache,
    IEventShareStreamBroadcaster streamBroadcaster,
    IConfiguration configuration) : AppController
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

    [HttpGet("{token}/stream")]
    [Produces("text/event-stream", "application/json")]
    [SwaggerOperation(
        Summary = "Luồng cập nhật trực tiếp của báo cáo chia sẻ (Server-Sent Events)",
        Description = "Giữ kết nối mở và gửi sự kiện text/event-stream mỗi khi tổng quan đã trả/còn nợ của đợt được chia sẻ thay đổi (event: updated) - client tự gọi lại GET .../shares/{token} (và QR nếu đang hiển thị) khi nhận sự kiện; luồng không mang theo dữ liệu báo cáo. Khi liên kết bị chủ sổ thu hồi/tạo lại (event: revoked) hoặc tự hết hạn (event: expired), gửi sự kiện kết thúc rồi đóng kết nối. Có bình luận giữ-kết-nối định kỳ. Không cần token đăng nhập. Token không tồn tại/đã hết hạn/đã thu hồi ngay khi kết nối trả về 404 (chưa ghi byte nào).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Kết nối SSE thành công.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Liên kết chia sẻ không tồn tại hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> StreamPublicAsync([FromRoute] string token, CancellationToken cancellationToken)
    {
        // Validate BEFORE writing anything (same LookupAsync the plain GET uses). Thrown before any byte
        // is written, so ErrorHandlerFilter still wraps this into the normal 404 16000 JSON envelope -
        // verified against the File()-returning export/QR actions, which throw from the service the same
        // way before ever calling File(...).
        _ = await shareLinkCache.LookupAsync(token, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.ShareLinkNotFoundOrExpired, MessageKeys.Error.ShareLinkNotFoundOrExpired);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // defense in depth; nginx also gets a dedicated location (Step 8)

        using var subscription = streamBroadcaster.Subscribe(token);
        await WriteFrameAsync("connected", cancellationToken); // OQ small, harmless "the pipe is live" ping

        var heartbeat = TimeSpan.FromSeconds(configuration.GetValue("Share:StreamHeartbeatSeconds", 20));
        using var timer = new PeriodicTimer(heartbeat);

        // Both tasks are long-lived across iterations - only the one that actually completed gets
        // replaced. PeriodicTimer.WaitForNextTickAsync() throws if called again while a previous call
        // is still pending, and re-issuing ReadAsync every iteration would abandon a still-pending read,
        // letting a "zombie" reader steal a later signal out from under the live one.
        var signalTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
        var tickTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await Task.WhenAny(signalTask, tickTask) == signalTask)
            {
                var signal = await signalTask;
                var name = signal.Type switch
                {
                    EventShareStreamSignalType.Revoked => "revoked",
                    EventShareStreamSignalType.Expired => "expired",
                    _ => "updated"
                };
                await WriteFrameAsync(name, cancellationToken);
                if (signal.Type != EventShareStreamSignalType.Updated)
                    break; // terminal
                signalTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
            }
            else
            {
                // Heartbeat tick doubles as the natural-expiry re-check (OQ1) - nobody has to actively
                // revoke for an aged-out link to close a still-open tab.
                if (await shareLinkCache.LookupAsync(token, cancellationToken) is null)
                {
                    await WriteFrameAsync("expired", cancellationToken);
                    break;
                }
                await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                tickTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();
            }
        }

        return new EmptyResult();

        async Task WriteFrameAsync(string eventName, CancellationToken ct)
        {
            await Response.WriteAsync($"event: {eventName}\ndata: {{}}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
