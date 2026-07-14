using Asp.Versioning;
using FairShareMonApi.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FairShareMonApi.Controllers;

/// <summary>
/// Base controller: versioned route + responses wrapped into the ApiResult envelope.
/// All controllers derive from it. LOCKED - do not modify this file without explicit,
/// file-specific permission in the current request (see CLAUDE.md).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ResponseWrapped]
public abstract class AppController : ControllerBase
{
    /// <summary>
    /// The current authenticated user. Throws a 401 <see cref="ErrorException"/> when the request
    /// is anonymous - only access it from endpoints that require authentication.
    /// </summary>
    protected AuthenticatedUser AuthenticatedUser =>
        HttpContext.RequestServices.GetRequiredService<IContextAuthenticated>().AuthenticatedUser
        ?? throw new ErrorException(ErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
}
