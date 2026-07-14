using System.Text.Encodings.Web;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FairShareMonApi.Auth;

/// <summary>
/// Default authentication scheme: extracts the Bearer token from the Authorization header and
/// delegates validation to <see cref="ITokenValidator"/> - it contains no token logic itself.
/// <c>[AllowAnonymous]</c> endpoints are honored by the authorization middleware (this handler
/// simply reports NoResult/Fail); the challenge writes a 401 in the ApiResult envelope.
/// </summary>
public sealed class OpaqueTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITokenValidator tokenValidator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpaqueToken";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawToken = authorization[BearerPrefix.Length..].Trim();
        if (rawToken.Length == 0)
            return AuthenticateResult.NoResult();

        var user = await tokenValidator.ValidateAsync(rawToken, Context.RequestAborted);
        if (user is null)
            return AuthenticateResult.Fail("Invalid or expired token.");

        return AuthenticateResult.Success(new AuthenticationTicket(user.ToPrincipal(Scheme.Name), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = ApiResult.Failure(ErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
        Response.StatusCode = result.StatusCode;
        await Response.WriteAsJsonAsync<object>(result, Context.RequestAborted);
    }

    /// <summary>
    /// Wraps genuine authorization-policy failures (403) in the ApiResult envelope, symmetric with
    /// the challenge. Business-level ownership misses never reach here - they return 404 per the
    /// Resource Owned rule (404, never 403).
    /// </summary>
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        var result = ApiResult.Failure(ErrorCodes.Forbidden, "Bạn không có quyền thực hiện thao tác này.");
        Response.StatusCode = result.StatusCode;
        await Response.WriteAsJsonAsync<object>(result, Context.RequestAborted);
    }
}
