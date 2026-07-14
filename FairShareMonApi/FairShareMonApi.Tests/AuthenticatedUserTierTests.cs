using System.Security.Claims;
using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the M10 tier claim on <see cref="AuthenticatedUser"/> (R1 / OQ8a). Proves the
/// <c>Tier</c> value round-trips through <c>ToClaims()</c> / <c>ToPrincipal()</c> /
/// <c>FromPrincipal(...)</c>, and that a principal carrying no tier claim (a token issued before M10) or
/// an empty tier resolves to <see cref="UserTiers.Free"/> - the fail-safe default. No DB.
/// </summary>
public class AuthenticatedUserTierTests
{
    private const string Scheme = "TestScheme";
    private const string Id = "0198a5c2-0000-7000-8000-00000000d001";

    [Theory]
    [InlineData(UserTiers.Premium)]
    [InlineData(UserTiers.Free)]
    public void ToClaims_FromPrincipal_RoundTripsTier(string tier)
    {
        var original = new AuthenticatedUser { Id = Id, Username = "an", Tier = tier };

        var restored = AuthenticatedUser.FromPrincipal(original.ToPrincipal(Scheme));

        Assert.NotNull(restored);
        Assert.Equal(Id, restored!.Id);
        Assert.Equal("an", restored.Username);
        Assert.Equal(tier, restored.Tier);
    }

    [Fact]
    public void FromPrincipal_NoTierClaim_DefaultsToFree()
    {
        // A token issued before M10 carries only NameIdentifier + Name (no "tier" claim).
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Id),
            new Claim(ClaimTypes.Name, "an")
        ], Scheme);

        var restored = AuthenticatedUser.FromPrincipal(new ClaimsPrincipal(identity));

        Assert.NotNull(restored);
        Assert.Equal(UserTiers.Free, restored!.Tier); // fail-safe: absent -> Free
    }

    [Fact]
    public void FromPrincipal_EmptyTierClaim_DefaultsToFree()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Id),
            new Claim(ClaimTypes.Name, "an"),
            new Claim("tier", string.Empty)
        ], Scheme);

        var restored = AuthenticatedUser.FromPrincipal(new ClaimsPrincipal(identity));

        Assert.NotNull(restored);
        Assert.Equal(UserTiers.Free, restored!.Tier);
    }

    [Fact]
    public void DefaultTier_WhenNotSet_IsFree()
    {
        var user = new AuthenticatedUser { Id = Id, Username = "an" };

        Assert.Equal(UserTiers.Free, user.Tier); // the property default is Free
    }

    [Fact]
    public void FromPrincipal_NoIdentifier_ReturnsNull()
    {
        var identity = new ClaimsIdentity([new Claim("tier", UserTiers.Premium)], Scheme);

        Assert.Null(AuthenticatedUser.FromPrincipal(new ClaimsPrincipal(identity)));
    }
}
