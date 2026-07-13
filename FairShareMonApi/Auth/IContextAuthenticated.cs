using DiDecoration.Attributes;

namespace FairShareMonApi.Auth;

/// <summary>Accessor for the current request's authenticated user (null when anonymous).</summary>
public interface IContextAuthenticated
{
    AuthenticatedUser? AuthenticatedUser { get; }
}

[ScopedService(typeof(IContextAuthenticated))]
public sealed class ContextAuthenticated(IHttpContextAccessor httpContextAccessor) : IContextAuthenticated
{
    public AuthenticatedUser? AuthenticatedUser
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            return Auth.AuthenticatedUser.FromPrincipal(principal);
        }
    }
}
