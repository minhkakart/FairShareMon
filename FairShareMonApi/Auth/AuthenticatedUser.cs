using System.Security.Claims;
using FairShareMonApi.Constants;

namespace FairShareMonApi.Auth;

/// <summary>
/// Identity of the authenticated user, materialized from token validation and carried through the
/// request as claims. <c>Id</c> is the user's UUID string. <c>Tier</c> is the user's
/// <see cref="UserTiers"/> value captured on the auth path (M10) so tier guards/gates read it without
/// a per-request DB hit; a missing/unknown tier claim resolves to <see cref="UserTiers.Free"/>
/// (fail-safe).
/// </summary>
public class AuthenticatedUser
{
    private const string TierClaimType = "tier";

    public required string Id { get; init; }

    public required string Username { get; init; }

    /// <summary>The caller's tier (<see cref="UserTiers"/>); FREE when absent/unknown (fail-safe).</summary>
    public string Tier { get; init; } = UserTiers.Free;

    public ClaimsPrincipal ToPrincipal(string authenticationScheme) =>
        new(new ClaimsIdentity(ToClaims(), authenticationScheme));

    public IEnumerable<Claim> ToClaims() =>
    [
        new Claim(ClaimTypes.NameIdentifier, Id),
        new Claim(ClaimTypes.Name, Username),
        new Claim(TierClaimType, string.IsNullOrEmpty(Tier) ? UserTiers.Free : Tier)
    ];

    public static AuthenticatedUser? FromPrincipal(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
            return null;

        var tier = principal.FindFirstValue(TierClaimType);
        return new AuthenticatedUser
        {
            Id = id,
            Username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            Tier = string.IsNullOrEmpty(tier) ? UserTiers.Free : tier
        };
    }
}
