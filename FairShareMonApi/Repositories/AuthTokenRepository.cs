using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Joined projection of an <c>auth_tokens</c> row and its user. Returned for revoked rows too so
/// the token service can distinguish "revoked" (reuse-detection cascade) from "unknown".
/// </summary>
public record AuthTokenLookup(
    string UserUuid,
    string Username,
    string TokenType,
    string PairUuid,
    DateTime ExpiresAt,
    DateTime? RevokedAt);

/// <summary>
/// Data access for the <c>auth_tokens</c> whitelist. Rotation/logout soft-revoke (set
/// <c>revoked_at</c>, row kept until natural expiry); the password-change kill switch and the
/// reuse-detection cascade hard-delete.
/// </summary>
public interface IAuthTokenRepository : IBaseRepository, IQueryRepository<AuthToken>
{
    /// <summary>Inserts the ACCESS + REFRESH rows of one issuance in a single transaction. False when the user is unknown.</summary>
    Task<bool> AddPairAsync(
        string userUuid,
        string pairUuid,
        string accessTokenHash,
        DateTime accessExpiresAt,
        string refreshTokenHash,
        DateTime refreshExpiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts a single whitelist row (used by the whitelist store). False when the user is unknown.</summary>
    Task<bool> AddAsync(
        string userUuid,
        string tokenHash,
        string tokenType,
        string pairUuid,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>Hash -> joined row including revoked ones; null when the hash is unknown.</summary>
    Task<AuthTokenLookup?> GetByHashWithUserAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Soft-revokes every not-yet-revoked row of the pair. Returns ALL of the pair's token hashes (for cache deletion).</summary>
    Task<IReadOnlyList<string>> RevokeByPairUuidAsync(string pairUuid, CancellationToken cancellationToken = default);

    /// <summary>Soft-revokes a single row by hash (used by the whitelist store). False when the hash is unknown.</summary>
    Task<bool> RevokeByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically soft-revokes a single still-active row by hash (a single conditional
    /// <c>revoked_at IS NULL</c> UPDATE). Returns true only if this call performed the transition -
    /// used to claim a refresh token for rotation so two concurrent refreshes can't both rotate;
    /// the loser (false) treats it as reuse.
    /// </summary>
    Task<bool> TryRevokeActiveByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes every row of the user (password-change kill switch, reuse-detection cascade). Returns the deleted hashes.</summary>
    Task<IReadOnlyList<string>> DeleteAllByUserIdAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>Opportunistically purges rows past their expiry, revoked or not. Returns the purge count.</summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IAuthTokenRepository))]
public sealed class AuthTokenRepository(AppDbContext dbContext) : BaseRepository(dbContext), IAuthTokenRepository
{
    public IQueryable<AuthToken> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<AuthToken>(tracking, includeDeleted);

    public Task<bool> AddPairAsync(
        string userUuid,
        string pairUuid,
        string accessTokenHash,
        DateTime accessExpiresAt,
        string refreshTokenHash,
        DateTime refreshExpiresAt,
        CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return false;
            }

            db.AuthTokens.Add(NewToken(userId.Value, accessTokenHash, Constants.TokenTypes.Access, pairUuid, accessExpiresAt));
            db.AuthTokens.Add(NewToken(userId.Value, refreshTokenHash, Constants.TokenTypes.Refresh, pairUuid, refreshExpiresAt));
            return true;
        }, cancellationToken);

    public Task<bool> AddAsync(
        string userUuid,
        string tokenHash,
        string tokenType,
        string pairUuid,
        DateTime expiresAt,
        CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return false;
            }

            db.AuthTokens.Add(NewToken(userId.Value, tokenHash, tokenType, pairUuid, expiresAt));
            return true;
        }, cancellationToken);

    public Task<AuthTokenLookup?> GetByHashWithUserAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => new AuthTokenLookup(
                token.User.Uuid,
                token.User.Username,
                token.TokenType,
                token.PairUuid,
                token.ExpiresAt,
                token.RevokedAt))
            .FirstOrDefaultAsync(ct), cancellationToken);

    public Task<IReadOnlyList<string>> RevokeByPairUuidAsync(string pairUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, _) =>
        {
            var tokens = await db.AuthTokens
                .Where(token => token.PairUuid == pairUuid)
                .ToListAsync(cancellationToken);

            var now = AppDateTime.Now;
            foreach (var token in tokens.Where(token => token.RevokedAt == null))
                token.RevokedAt = now;

            return (IReadOnlyList<string>)tokens.Select(token => token.TokenHash).ToList();
        }, cancellationToken);

    public Task<bool> RevokeByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var token = await db.AuthTokens.FirstOrDefaultAsync(existing => existing.TokenHash == tokenHash, cancellationToken);
            if (token is null)
            {
                transaction.NoCommit();
                return false;
            }

            token.RevokedAt ??= AppDateTime.Now;
            return true;
        }, cancellationToken);

    public Task<bool> TryRevokeActiveByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, _) =>
        {
            var now = AppDateTime.Now;
            var affected = await db.AuthTokens
                .Where(token => token.TokenHash == tokenHash && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
            return affected == 1;
        }, cancellationToken);

    public Task<IReadOnlyList<string>> DeleteAllByUserIdAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, _) =>
        {
            var hashes = await db.AuthTokens
                .Where(token => token.User.Uuid == userUuid)
                .Select(token => token.TokenHash)
                .ToListAsync(cancellationToken);

            if (hashes.Count > 0)
                await db.AuthTokens
                    .Where(token => token.User.Uuid == userUuid)
                    .ExecuteDeleteAsync(cancellationToken);

            return (IReadOnlyList<string>)hashes;
        }, cancellationToken);

    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync((db, _) =>
        {
            var now = AppDateTime.Now;
            return db.AuthTokens
                .Where(token => token.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static AuthToken NewToken(ulong userId, string tokenHash, string tokenType, string pairUuid, DateTime expiresAt) =>
        new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            TokenType = tokenType,
            PairUuid = pairUuid,
            ExpiresAt = expiresAt
        };
}
