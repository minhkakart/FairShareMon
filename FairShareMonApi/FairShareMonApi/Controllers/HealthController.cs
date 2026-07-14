using FairShareMonApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

[AllowAnonymous]
public class HealthController : AppController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Kiểm tra tình trạng hoạt động của hệ thống")]
    public IActionResult Get() => ApiResult.SuccessMessage("Hệ thống hoạt động bình thường.");
}
