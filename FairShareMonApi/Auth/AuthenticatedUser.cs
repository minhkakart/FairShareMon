using System.Security.Claims;

namespace FairShareMonApi.Auth;

/// <summary>
/// Identity of the authenticated user, materialized from token validation and carried through the
/// request as claims. <c>Id</c> is the user's UUID string.
/// </summary>
public class AuthenticatedUser
{
    public required string Id { get; init; }

    public required string Username { get; init; }

    public ClaimsPrincipal ToPrincipal(string authenticationScheme) =>
        new(new ClaimsIdentity(ToClaims(), authenticationScheme));

    public IEnumerable<Claim> ToClaims() =>
    [
        new Claim(ClaimTypes.NameIdentifier, Id),
        new Claim(ClaimTypes.Name, Username)
    ];

    public static AuthenticatedUser? FromPrincipal(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
            return null;

        return new AuthenticatedUser
        {
            Id = id,
            Username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty
        };
    }
}
