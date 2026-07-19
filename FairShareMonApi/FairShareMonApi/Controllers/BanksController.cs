using Asp.Versioning;
using FairShareMonApi.Models;
using FairShareMonApi.Models.Banks;
using FairShareMonApi.Services.Api.Banks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Danh mục ngân hàng (The-ideal.md §3.10): trả về danh sách ngân hàng để hiển thị bộ chọn ngân hàng trong
/// ví. Endpoint yêu cầu access token hợp lệ (không <c>[AllowAnonymous]</c>) nhưng là dữ liệu tham chiếu chung
/// nên không giới hạn theo gói Premium. Không bao giờ lỗi nhờ snapshot tĩnh dự phòng. Thin - toàn bộ nghiệp
/// vụ nằm trong <see cref="IBankDirectoryService"/>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/banks")]
public class BanksController(IBankDirectoryService bankDirectoryService) : AppController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Danh sách ngân hàng",
        Description = "Trả về danh mục ngân hàng (BIN, mã, tên, tên ngắn, URL logo) để hiển thị bộ chọn ngân hàng.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Lấy danh sách ngân hàng thành công.", typeof(ApiResult<IReadOnlyList<BankResponse>>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        ApiResult<IReadOnlyList<BankResponse>>.Success(
            await bankDirectoryService.ListAsync(cancellationToken));
}
