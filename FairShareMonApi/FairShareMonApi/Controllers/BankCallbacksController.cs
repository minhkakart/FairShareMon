using System.Text.Json;
using Asp.Versioning;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Localization.Resources;
using FairShareMonApi.Models;
using FairShareMonApi.Models.BankCallbacks;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.BankCallbacks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Inbound bank-transaction webhooks + the owner-facing review list (planning/bank-callback-settlement.md).
/// The POST receiver is <see cref="AllowAnonymousAttribute"/> at the ACTION level (Decision Log entry 9) -
/// its "authentication" is the provider's own API key/signature, verified inside
/// <see cref="IBankCallbackParser.Verify"/>, never the app's opaque-token scheme. The GET review list stays
/// under the default authenticated fallback policy and is deliberately ungated (OQ9) - a Free-tier owner
/// simply always sees an empty list, since correlation codes are only ever created behind the already
/// Premium-gated QR-generation calls.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/bank-callbacks")]
public class BankCallbacksController(
    IBankCallbackParserResolver parserResolver,
    IBankCallbackService bankCallbackService,
    IBankTransactionCallbackRepository callbackRepository,
    IStringLocalizer<StringResources> localizer) : AppController
{
    [AllowAnonymous]
    [HttpPost("{provider}")]
    [SwaggerOperation(
        Summary = "Nhận webhook giao dịch ngân hàng",
        Description = "Điểm nhận webhook giao dịch ngân hàng đến từ một nhà cung cấp tổng hợp giao dịch (ví dụ SePay). Không dùng token đăng nhập - xác thực bằng khóa API/chữ ký riêng của nhà cung cấp. Giao dịch khớp mã liên kết và đúng số tiền sẽ tự động đánh dấu đã trả; giao dịch không khớp/không xác thực được vẫn được ghi nhận và luôn trả về 200 cho nhà cung cấp.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã nhận và xử lý giao dịch (dù khớp, không khớp hay bị giữ lại).", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu giao dịch ngân hàng không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Xác thực webhook không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không hỗ trợ nhà cung cấp này.", typeof(ApiResult))]
    public async Task<IActionResult> ReceiveAsync([FromRoute] string provider, [FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var parser = parserResolver.Resolve(provider)
            ?? throw new ErrorException(ErrorCodes.BankCallbackProviderUnknown, MessageKeys.Error.BankCallbackProviderUnknown);

        if (!parser.Verify(Request, payload))
            throw new ErrorException(ErrorCodes.BankCallbackVerificationFailed, MessageKeys.Error.BankCallbackVerificationFailed);

        var transactionEvent = parser.Parse(payload)
            ?? throw new ErrorException(ErrorCodes.BankCallbackPayloadInvalid, MessageKeys.Error.BankCallbackPayloadInvalid);

        await bankCallbackService.ProcessAsync(parser.ProviderKey, transactionEvent, payload.GetRawText(), cancellationToken);

        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.BankCallbackReceived].Value);
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách giao dịch ngân hàng đã nhận",
        Description = "Trả về danh sách giao dịch ngân hàng gần đây đã khớp với tài khoản này (đã áp dụng, không khớp mã, sai số tiền, đã trả từ trước, hoặc xác thực nội bộ thất bại), mới nhất trước - giúp chủ sổ biết lý do một giao dịch không tự động đánh dấu đã trả. Không phân theo Free/Premium.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách giao dịch ngân hàng thành công.", typeof(ApiResult<IReadOnlyList<BankTransactionCallbackResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync([FromQuery] int limit = 20, [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
    {
        var (items, _) = await callbackRepository.ListByUserAsync(AuthenticatedUser.Id, limit, offset, cancellationToken);

        return ApiResult<IReadOnlyList<BankTransactionCallbackResponse>>.Success(items.Select(ToResponse).ToList());
    }

    private static BankTransactionCallbackResponse ToResponse(Database.Entities.BankTransactionCallback callback)
    {
        var code = callback.MatchedCorrelationCode;
        return new BankTransactionCallbackResponse
        {
            Uuid = callback.Uuid,
            ProviderKey = callback.ProviderKey,
            Amount = callback.Amount,
            Content = callback.Content,
            Outcome = callback.Outcome.ToString(),
            TransactionAt = callback.TransactionAt,
            AppliedAt = callback.AppliedAt,
            MatchedTargetType = code is null ? null : (code.ExpenseId is not null ? nameof(CorrelationTargetKind.Share) : nameof(CorrelationTargetKind.EventMember)),
            MatchedExpenseUuid = code?.Expense?.Uuid,
            MatchedEventUuid = code?.Event?.Uuid,
            MatchedMemberUuid = code?.Member?.Uuid,
            MemberName = code?.Member?.Name,
            CreatedAt = callback.CreatedAt
        };
    }
}
