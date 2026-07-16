using FairShareMonApi.Models;
using Microsoft.AspNetCore.Authorization;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FairShareMonApi.Controllers;

[AllowAnonymous]
public class HealthController(IStringLocalizer<StringResources> localizer) : AppController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Kiểm tra tình trạng hoạt động của hệ thống")]
    public IActionResult Get() => ApiResult.SuccessMessage(localizer[MessageKeys.Success.HealthOk].Value);
}
