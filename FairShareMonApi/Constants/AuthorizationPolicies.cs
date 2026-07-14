namespace FairShareMonApi.Constants;

/// <summary>
/// Named authorization policies (M11 Admin suite, OQ11). The <see cref="Admin"/> policy requires the
/// <see cref="RoleClaimType"/> claim to equal <see cref="UserRoles.Admin"/>; a non-admin fails it and
/// gets the already-wired 403 <c>Forbidden 1004</c>. The role claim is written by
/// <c>AuthenticatedUser.ToClaims()</c>.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Admin = "Admin";

    /// <summary>Claim type carrying the caller's <see cref="UserRoles"/> value on the principal.</summary>
    public const string RoleClaimType = "role";
}
