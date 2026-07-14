using DiDecoration.Attributes;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Auth;

/// <summary>
/// Per-request token validation: hash the raw bearer token, look it up in the whitelist (Redis
/// first, DB fallback - via <see cref="ITokenWhitelistStore"/>), and require an unexpired ACCESS
/// token. The cache entry carries the username, so no per-request DB hit is needed on cache hits.
/// </summary>
[ScopedService(typeof(ITokenValidator))]
public sealed class TokenValidator(ITokenWhitelistStore whitelistStore) : ITokenValidator
{
    public async Task<AuthenticatedUser?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var entry = await whitelistStore.LookupAsync(TokenHasher.Sha256Hex(rawToken), cancellationToken);
        if (entry is null || entry.TokenType != TokenTypes.Access || entry.ExpiresAt <= AppDateTime.Now)
            return null;

        return new AuthenticatedUser
        {
            Id = entry.UserId,
            Username = entry.Username,
            Tier = string.IsNullOrEmpty(entry.Tier) ? UserTiers.Free : entry.Tier,
            Role = string.IsNullOrEmpty(entry.Role) ? UserRoles.User : entry.Role
        };
    }
}
