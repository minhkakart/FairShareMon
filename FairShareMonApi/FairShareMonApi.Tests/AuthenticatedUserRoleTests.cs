using System.Security.Claims;
using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Repositories;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the M11 role claim on <see cref="AuthenticatedUser"/> (R2), mirroring the M10
/// tier tests. Proves the <c>Role</c> value round-trips through <c>ToClaims()</c> / <c>ToPrincipal()</c> /
/// <c>FromPrincipal(...)</c>; a principal carrying no role claim (a token issued before M11) or an empty
/// role resolves to <see cref="UserRoles.User"/> (fail-safe - NEVER ADMIN); an unknown role value is
/// carried verbatim but is not <see cref="UserRoles.Admin"/> so the Admin policy denies it. Also proves
/// the two positional records (<see cref="TokenWhitelistEntry"/> / <see cref="AuthTokenLookup"/>) default
/// <c>Role = USER</c> when the trailing arg is omitted (back-compat for pre-M11 cached/projected rows).
/// No DB.
/// </summary>
public class AuthenticatedUserRoleTests
{
    private const string Scheme = "TestScheme";
    private const string Id = "0198a5c2-0000-7000-8000-00000000e001";

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.User)]
    public void ToClaims_FromPrincipal_RoundTripsRole(string role)
    {
        var original = new AuthenticatedUser { Id = Id, Username = "an", Role = role };

        var restored = AuthenticatedUser.FromPrincipal(original.ToPrincipal(Scheme));

        Assert.NotNull(restored);
        Assert.Equal(Id, restored!.Id);
        Assert.Equal("an", restored.Username);
        Assert.Equal(role, restored.Role);
    }

    [Fact]
    public void ToClaims_FromPrincipal_RoundTripsRoleAndTierTogether()
    {
        var original = new AuthenticatedUser { Id = Id, Username = "an", Tier = UserTiers.Premium, Role = UserRoles.Admin };

        var restored = AuthenticatedUser.FromPrincipal(original.ToPrincipal(Scheme));

        Assert.NotNull(restored);
        Assert.Equal(UserTiers.Premium, restored!.Tier);
        Assert.Equal(UserRoles.Admin, restored.Role);
    }

    [Fact]
    public void FromPrincipal_NoRoleClaim_DefaultsToUser()
    {
        // A token issued before M11 carries only NameIdentifier + Name (+ tier), no "role" claim.
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Id),
            new Claim(ClaimTypes.Name, "an")
        ], Scheme);

        var restored = AuthenticatedUser.FromPrincipal(new ClaimsPrincipal(identity));

        Assert.NotNull(restored);
        Assert.Equal(UserRoles.User, restored!.Role); // fail-safe: absent -> USER, never ADMIN
    }

    [Fact]
    public void FromPrincipal_EmptyRoleClaim_DefaultsToUser()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Id),
            new Claim(ClaimTypes.Name, "an"),
            new Claim(AuthorizationPolicies.RoleClaimType, string.Empty)
        ], Scheme);

        var restored = AuthenticatedUser.FromPrincipal(new ClaimsPrincipal(identity));

        Assert.NotNull(restored);
        Assert.Equal(UserRoles.User, restored!.Role);
    }

    [Fact]
    public void FromPrincipal_UnknownRoleClaim_CarriedVerbatimButNotAdmin()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Id),
            new Claim(ClaimTypes.Name, "an"),
            new Claim(AuthorizationPolicies.RoleClaimType, "SUPERUSER")
        ], Scheme);

        var restored = AuthenticatedUser.FromPrincipal(new ClaimsPrincipal(identity));

        Assert.NotNull(restored);
        Assert.NotEqual(UserRoles.Admin, restored!.Role); // an unknown value is never ADMIN -> Admin policy denies
    }

    [Fact]
    public void DefaultRole_WhenNotSet_IsUser()
    {
        var user = new AuthenticatedUser { Id = Id, Username = "an" };

        Assert.Equal(UserRoles.User, user.Role); // the property default is USER
    }

    [Fact]
    public void ToClaims_EmitsExactlyOneRoleClaim_WithTheRoleValue()
    {
        var claims = new AuthenticatedUser { Id = Id, Username = "an", Role = UserRoles.Admin }.ToClaims();

        var roleClaim = Assert.Single(claims, claim => claim.Type == AuthorizationPolicies.RoleClaimType);
        Assert.Equal(UserRoles.Admin, roleClaim.Value);
    }

    // ---- Positional records default Role = USER (back-compat for pre-M11 cached/projected rows) ------

    [Fact]
    public void TokenWhitelistEntry_TrailingRoleOmitted_DefaultsToUser()
    {
        var entry = new TokenWhitelistEntry(Id, DateTime.UtcNow.AddMinutes(30), "an", TokenTypes.Access, "pair-1");

        Assert.Equal(UserRoles.User, entry.Role);
        Assert.Equal(UserTiers.Free, entry.Tier); // the M10 trailing default still holds too
    }

    [Fact]
    public void TokenWhitelistEntry_TierGivenRoleOmitted_RoleStillDefaultsToUser()
    {
        var entry = new TokenWhitelistEntry(Id, DateTime.UtcNow.AddMinutes(30), "an", TokenTypes.Access, "pair-1", UserTiers.Premium);

        Assert.Equal(UserTiers.Premium, entry.Tier);
        Assert.Equal(UserRoles.User, entry.Role);
    }

    [Fact]
    public void AuthTokenLookup_TrailingRoleOmitted_DefaultsToUser()
    {
        var lookup = new AuthTokenLookup(Id, "an", TokenTypes.Access, "pair-1", DateTime.UtcNow.AddMinutes(30), null);

        Assert.Equal(UserRoles.User, lookup.Role);
        Assert.Equal(UserTiers.Free, lookup.Tier);
    }
}
