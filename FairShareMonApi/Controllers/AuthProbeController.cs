using FairShareMonApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FairShareMonApi.Controllers;

/// <summary>
/// TEMPORARY probe endpoint: exists only so integration tests can verify the auth pipeline
/// (stub validator -> 401 wrapped in ApiResult). Hidden from Swagger. Remove once the first
/// real [Authorize]-guarded endpoint lands.
/// </summary>
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
public class AuthProbeController : AppController
{
    [HttpGet]
    public IActionResult Get() => ApiResult.SuccessMessage("Đã xác thực thành công.");
}
