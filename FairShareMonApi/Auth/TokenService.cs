using System.Security.Cryptography;
using DiDecoration.Attributes;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Repositories;
using FairShareMonApi.Utils;
using Microsoft.AspNetCore.WebUtilities;
using StackExchange.Redis;

namespace FairShareMonApi.Auth;

/// <summary>
/// Opaque stateful token lifecycle. Raw tokens are 32 bytes of CSPRNG output, Base64Url-encoded
/// (43 chars, 256-bit entropy); only their SHA-256 hashes are persisted (auth_tokens) and cached
/// (Redis, best-effort per the OQ7 decision). Lifetimes come from <c>Auth:AccessTokenLifetime</c>
/// (default 30 minutes) and <c>Auth:RefreshTokenLifetime</c> (default 30 days). Refresh performs
/// FULL pair rotation with reuse detection (OQ4): a revoked refresh token presented again revokes
/// ALL of the user's sessions.
/// </summary>
[ScopedService(typeof(ITokenService))]
public sealed class TokenService(
    IAuthTokenRepository authTokenRepository,
    IConnectionMultiplexer redis,
    IConfiguration configuration,
    ILogger<TokenService> logger) : ITokenService
{
    private const int TokenByteLength = 32;

    private readonly TimeSpan _accessTokenLifetime =
        configuration.GetValue("Auth:AccessTokenLifetime", TimeSpan.FromMinutes(30));

    private readonly TimeSpan _refreshTokenLifetime =
        configuration.GetValue("Auth:RefreshTokenLifetime", TimeSpan.FromDays(30));

    public async Task<TokenPair?> IssueAsync(string userId, string username, CancellationToken cancellationToken = default)
    {
        // Opportunistic purge (no scheduler by decision): expired rows, revoked or not.
        await authTokenRepository.DeleteExpiredAsync(cancellationToken);

        var now = AppDateTime.Now;
        var rawAccessToken = NewRawToken();
        var rawRefreshToken = NewRawToken();
        var accessExpiresAt = now.Add(_accessTokenLifetime);
        var refreshExpiresAt = now.Add(_refreshTokenLifetime);
        var pairUuid = Uuid.NewV7();
        var accessHash = TokenHasher.Sha256Hex(rawAccessToken);
        var refreshHash = TokenHasher.Sha256Hex(rawRefreshToken);

        var added = await authTokenRepository.AddPairAsync(
            userId, pairUuid, accessHash, accessExpiresAt, refreshHash, refreshExpiresAt, cancellationToken);
        if (!added)
            return null;

        // Best-effort cache priming; validation self-heals via DB fallback + backfill anyway.
        await TokenWhitelistStore.TryCacheAsync(redis, logger, accessHash,
            new TokenWhitelistEntry(userId, accessExpiresAt, username, TokenTypes.Access, pairUuid));
        await TokenWhitelistStore.TryCacheAsync(redis, logger, refreshHash,
            new TokenWhitelistEntry(userId, refreshExpiresAt, username, TokenTypes.Refresh, pairUuid));

        return new TokenPair(rawAccessToken, accessExpiresAt, rawRefreshToken, refreshExpiresAt);
    }

    public async Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var refreshHash = TokenHasher.Sha256Hex(refreshToken);
        var lookup = await authTokenRepository.GetByHashWithUserAsync(refreshHash, cancellationToken);
        if (lookup is null || lookup.TokenType != TokenTypes.Refresh || lookup.ExpiresAt <= AppDateTime.Now)
            return null;

        // Atomically claim this refresh token for rotation (conditional soft-revoke). Losing the
        // claim means it was already revoked - either a prior rotation/logout, or a concurrent
        // refresh that won the race. Both are reuse signals (OQ4): the atomic claim closes the
        // gap where two simultaneous refreshes could each pass a plain revoked-check and rotate.
        if (!await authTokenRepository.TryRevokeActiveByHashAsync(refreshHash, cancellationToken))
        {
            logger.LogWarning("Refresh token reuse detected for user {UserUuid} - revoking all sessions.", lookup.UserUuid);
            await RevokeAllAsync(lookup.UserUuid, cancellationToken);
            return null;
        }

        // Won the claim (the refresh row is now revoked): revoke the paired access token too, then issue a fresh pair.
        await RevokePairAsync(lookup.PairUuid, cancellationToken);
        return await IssueAsync(lookup.UserUuid, lookup.Username, cancellationToken);
    }

    public async Task<bool> RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var lookup = await authTokenRepository.GetByHashWithUserAsync(TokenHasher.Sha256Hex(rawToken), cancellationToken);
        if (lookup is null)
            return false;

        await RevokePairAsync(lookup.PairUuid, cancellationToken);
        return true;
    }

    public async Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        var deletedHashes = await authTokenRepository.DeleteAllByUserIdAsync(userId, cancellationToken);
        foreach (var tokenHash in deletedHashes)
            await TokenWhitelistStore.TryDeleteCachedAsync(redis, logger, tokenHash);

        return deletedHashes.Count;
    }

    private async Task RevokePairAsync(string pairUuid, CancellationToken cancellationToken)
    {
        var pairHashes = await authTokenRepository.RevokeByPairUuidAsync(pairUuid, cancellationToken);
        foreach (var tokenHash in pairHashes)
            await TokenWhitelistStore.TryDeleteCachedAsync(redis, logger, tokenHash);
    }

    private static string NewRawToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenByteLength));
}
