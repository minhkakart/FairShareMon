using Asp.Versioning;
using FairShareMonApi.Models;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Bank account (ví) endpoints (The-ideal.md §3.10): list / get / create / update / set-default /
/// delete the current user's receiving bank accounts. All actions require a valid access token and are
/// resource-owned - an account that isn't the caller's yields 404 (never 403). Exactly one account is
/// the default whenever the wallet is non-empty (first account auto-default; atomic swap on
/// set-default; delete-of-default promotes another). Thin - all business logic in
/// <see cref="IBankAccountsService"/>.
/// </summary>
/// <remarks>
/// The route is set explicitly to the kebab-case resource <c>bank-accounts</c> (OQ15): the base
/// <c>[controller]</c> token would render the multi-word controller name as <c>BankAccounts</c>, so a
/// derived <see cref="RouteAttribute"/> overrides it while keeping the versioned prefix.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/bank-accounts")]
public class BankAccountsController(IBankAccountsService bankAccountsService, IStringLocalizer<StringResources> localizer) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách tài khoản ngân hàng",
        Description = "Trả về các tài khoản ngân hàng trong ví của tài khoản, sắp xếp tài khoản mặc định trước rồi đến tài khoản tạo gần nhất.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách tài khoản ngân hàng thành công.", typeof(ApiResult<IReadOnlyList<BankAccountResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<BankAccountResponse>>.Success(
            await bankAccountsService.ListAsync(AuthenticatedUser.Id, cancellationToken));

    [HttpGet("{uuid}")]
    [SwaggerOperation(
        Summary = "Chi tiết một tài khoản ngân hàng",
        Description = "Trả về thông tin một tài khoản ngân hàng trong ví theo UUID.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin tài khoản ngân hàng thành công.", typeof(ApiResult<BankAccountResponse>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy tài khoản ngân hàng.", typeof(ApiResult))]
    public async Task<IActionResult> GetAsync([FromRoute] string uuid, CancellationToken cancellationToken) =>
        ApiResult<BankAccountResponse>.Success(
            await bankAccountsService.GetAsync(AuthenticatedUser.Id, uuid, cancellationToken));

    [HttpPost]
    [SwaggerOperation(
        Summary = "Thêm tài khoản ngân hàng",
        Description = "Thêm một tài khoản ngân hàng vào ví (mã ngân hàng BIN, tên ngân hàng, số tài khoản, tên chủ tài khoản). Tài khoản đầu tiên trong ví tự động được đặt làm mặc định.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Thêm tài khoản ngân hàng thành công.", typeof(ApiResult<BankAccountResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Tính năng ví ngân hàng chỉ dành cho tài khoản Premium (nâng cấp để sử dụng).", typeof(ApiResult))]
    public async Task<IActionResult> CreateAsync([FromBody] CreateBankAccountRequest request, CancellationToken cancellationToken) =>
        ApiResult<BankAccountResponse>.Success(
            await bankAccountsService.CreateAsync(AuthenticatedUser.Id, request, cancellationToken));

    [HttpPut("{uuid}")]
    [SwaggerOperation(
        Summary = "Cập nhật tài khoản ngân hàng",
        Description = "Cập nhật thông tin một tài khoản ngân hàng. Không thể đổi cờ mặc định qua endpoint này (dùng đặt tài khoản mặc định).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Cập nhật tài khoản ngân hàng thành công.", typeof(ApiResult<BankAccountResponse>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Tính năng ví ngân hàng chỉ dành cho tài khoản Premium (nâng cấp để sử dụng).", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy tài khoản ngân hàng.", typeof(ApiResult))]
    public async Task<IActionResult> UpdateAsync([FromRoute] string uuid, [FromBody] UpdateBankAccountRequest request, CancellationToken cancellationToken) =>
        ApiResult<BankAccountResponse>.Success(
            await bankAccountsService.UpdateAsync(AuthenticatedUser.Id, uuid, request, cancellationToken));

    [HttpPut("{uuid}/default")]
    [SwaggerOperation(
        Summary = "Đặt tài khoản ngân hàng mặc định",
        Description = "Đặt một tài khoản làm mặc định: cờ mặc định của tài khoản cũ được gỡ và gán cho tài khoản này trong cùng một giao dịch.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã đặt tài khoản ngân hàng mặc định.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Tính năng ví ngân hàng chỉ dành cho tài khoản Premium (nâng cấp để sử dụng).", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy tài khoản ngân hàng.", typeof(ApiResult))]
    public async Task<IActionResult> SetDefaultAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await bankAccountsService.SetDefaultAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.BankAccountSetDefault].Value);
    }

    [HttpDelete("{uuid}")]
    [SwaggerOperation(
        Summary = "Xóa tài khoản ngân hàng",
        Description = "Xóa một tài khoản ngân hàng khỏi ví. Nếu xóa tài khoản mặc định và ví còn tài khoản khác thì tài khoản tạo gần nhất còn lại được đặt làm mặc định; xóa tài khoản cuối cùng thì ví trở về trạng thái rỗng.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Đã xóa tài khoản ngân hàng.", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Tính năng ví ngân hàng chỉ dành cho tài khoản Premium (nâng cấp để sử dụng).", typeof(ApiResult))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Không tìm thấy tài khoản ngân hàng.", typeof(ApiResult))]
    public async Task<IActionResult> DeleteAsync([FromRoute] string uuid, CancellationToken cancellationToken)
    {
        await bankAccountsService.DeleteAsync(AuthenticatedUser.Id, uuid, cancellationToken);
        return ApiResult.SuccessMessage(localizer[MessageKeys.Success.BankAccountDeleted].Value);
    }
}
