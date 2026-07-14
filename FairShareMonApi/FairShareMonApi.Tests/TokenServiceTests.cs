using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the opaque-token lifecycle over an in-memory fake repository. Redis is the
/// unreachable multiplexer, so every cache operation exercises the OQ7 warn-and-continue path -
/// nothing here needs a live server. Covers issuance shape/expiries, FULL pair rotation, the
/// reuse-detection revoke-all cascade (OQ4), pair-scoped logout revocation, and revoke-all.
/// </summary>
public class TokenServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000000aa";
    private const string Username = "bob";

    private readonly FakeAuthTokenRepository _repository = new();

    public TokenServiceTests() => _repository.AddKnownUser(UserUuid, Username);

    private TokenService CreateService(string accessLifetime = "00:30:00", string refreshLifetime = "30.00:00:00")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AccessTokenLifetime"] = accessLifetime,
                ["Auth:RefreshTokenLifetime"] = refreshLifetime
            })
            .Build();

        return new TokenService(_repository, UnreachableRedis.Instance, configuration, NullLogger<TokenService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_KnownUser_ReturnsDistinct43CharRawTokensWithConfiguredLifetimes()
    {
        var before = DateTime.UtcNow;
        var pair = await CreateService().IssueAsync(UserUuid, Username);
        var after = DateTime.UtcNow;

        Assert.NotNull(pair);
        Assert.Equal(43, pair!.AccessToken.Length); // 32 CSPRNG bytes, Base64Url
        Assert.Equal(43, pair.RefreshToken.Length);
        Assert.NotEqual(pair.AccessToken, pair.RefreshToken);
        Assert.InRange(pair.AccessTokenExpiresAt, before.AddMinutes(30), after.AddMinutes(30)); // OQ1: 30 minutes
        Assert.InRange(pair.RefreshTokenExpiresAt, before.AddDays(30), after.AddDays(30)); // OQ1: 30 days
    }

    [Fact]
    public async Task IssueAsync_CustomConfiguredLifetimes_AreHonored()
    {
        var before = DateTime.UtcNow;
        var pair = await CreateService(accessLifetime: "00:10:00", refreshLifetime: "1.00:00:00").IssueAsync(UserUuid, Username);
        var after = DateTime.UtcNow;

        Assert.NotNull(pair);
        Assert.InRange(pair!.AccessTokenExpiresAt, before.AddMinutes(10), after.AddMinutes(10));
        Assert.InRange(pair.RefreshTokenExpiresAt, before.AddDays(1), after.AddDays(1));
    }

    [Fact]
    public async Task IssueAsync_PersistsOnlySha256HashesSharingOnePairUuid()
    {
        var pair = await CreateService().IssueAsync(UserUuid, Username);

        Assert.Equal(2, _repository.Rows.Count);
        var accessRow = Assert.Single(_repository.Rows, row => row.TokenType == TokenTypes.Access);
        var refreshRow = Assert.Single(_repository.Rows, row => row.TokenType == TokenTypes.Refresh);
        Assert.Equal(TokenHasher.Sha256Hex(pair!.AccessToken), accessRow.TokenHash); // hash, never the raw token
        Assert.Equal(TokenHasher.Sha256Hex(pair.RefreshToken), refreshRow.TokenHash);
        Assert.Equal(accessRow.PairUuid, refreshRow.PairUuid); // one issuance = one shared pair id
        Assert.All(_repository.Rows, row => Assert.Null(row.RevokedAt));
    }

    [Fact]
    public async Task IssueAsync_UnknownUser_ReturnsNull()
    {
        var pair = await CreateService().IssueAsync("no-such-user-uuid", "ghost");

        Assert.Null(pair);
        Assert.Empty(_repository.Rows);
    }

    [Fact]
    public async Task IssueAsync_PurgesExpiredRowsOpportunistically()
    {
        _repository.SeedRow(UserUuid, Username, "expired-hash", TokenTypes.Access, "old-pair", DateTime.UtcNow.AddMinutes(-5));

        await CreateService().IssueAsync(UserUuid, Username);

        Assert.True(_repository.DeleteExpiredCallCount >= 1);
        Assert.DoesNotContain(_repository.Rows, row => row.TokenHash == "expired-hash");
    }

    [Fact]
    public async Task RefreshAsync_ValidRefreshToken_RotatesTheFullPair()
    {
        var service = CreateService();
        var oldPair = (await service.IssueAsync(UserUuid, Username))!;

        var newPair = await service.RefreshAsync(oldPair.RefreshToken);

        Assert.NotNull(newPair);
        Assert.NotEqual(oldPair.AccessToken, newPair!.AccessToken);
        Assert.NotEqual(oldPair.RefreshToken, newPair.RefreshToken);

        // Full rotation: BOTH old rows (refresh AND its paired access) are revoked...
        var oldAccessRow = _repository.FindByHash(TokenHasher.Sha256Hex(oldPair.AccessToken))!;
        var oldRefreshRow = _repository.FindByHash(TokenHasher.Sha256Hex(oldPair.RefreshToken))!;
        Assert.NotNull(oldAccessRow.RevokedAt);
        Assert.NotNull(oldRefreshRow.RevokedAt);

        // ...and the new pair is active under a NEW pair uuid.
        var newAccessRow = _repository.FindByHash(TokenHasher.Sha256Hex(newPair.AccessToken))!;
        Assert.Null(newAccessRow.RevokedAt);
        Assert.NotEqual(oldAccessRow.PairUuid, newAccessRow.PairUuid);
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ReturnsNullWithoutCascade()
    {
        var result = await CreateService().RefreshAsync("never-issued-token");

        Assert.Null(result);
        Assert.Empty(_repository.RevokeAllUserUuids);
    }

    [Fact]
    public async Task RefreshAsync_AccessTokenPresented_ReturnsNullWithoutRevokingAnything()
    {
        var service = CreateService();
        var pair = (await service.IssueAsync(UserUuid, Username))!;

        var result = await service.RefreshAsync(pair.AccessToken);

        Assert.Null(result); // an ACCESS token can never be exchanged for a new pair
        Assert.All(_repository.Rows, row => Assert.Null(row.RevokedAt));
        Assert.Empty(_repository.RevokeAllUserUuids);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredRefreshToken_ReturnsNullWithoutCascade()
    {
        var rawToken = "expired-refresh-raw-token";
        _repository.SeedRow(UserUuid, Username, TokenHasher.Sha256Hex(rawToken), TokenTypes.Refresh, "pair-e", DateTime.UtcNow.AddMinutes(-1));

        var result = await CreateService().RefreshAsync(rawToken);

        Assert.Null(result);
        Assert.Empty(_repository.RevokeAllUserUuids); // expired is not a theft signal
    }

    [Fact]
    public async Task RefreshAsync_RevokedRefreshTokenReused_RevokesAllUserSessions()
    {
        var service = CreateService();
        var stolenPair = (await service.IssueAsync(UserUuid, Username))!;
        await service.RefreshAsync(stolenPair.RefreshToken); // legitimate rotation revokes the old pair

        var reuse = await service.RefreshAsync(stolenPair.RefreshToken); // theft signal (OQ4)

        Assert.Null(reuse);
        Assert.Contains(UserUuid, _repository.RevokeAllUserUuids);
        Assert.Empty(_repository.Rows); // every session of the user is gone, including the rotated pair
    }

    [Fact]
    public async Task RevokeAsync_KnownAccessToken_RevokesTheWholePairAndReturnsTrue()
    {
        var service = CreateService();
        var pair = (await service.IssueAsync(UserUuid, Username))!;

        var revoked = await service.RevokeAsync(pair.AccessToken);

        Assert.True(revoked);
        Assert.All(_repository.Rows, row => Assert.NotNull(row.RevokedAt)); // logout kills access AND refresh
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_ReturnsFalse()
    {
        var revoked = await CreateService().RevokeAsync("never-issued-token");

        Assert.False(revoked);
    }

    [Fact]
    public async Task RevokeAllAsync_DeletesEveryRowOfTheUserAndReturnsCount()
    {
        var service = CreateService();
        await service.IssueAsync(UserUuid, Username);
        await service.IssueAsync(UserUuid, Username); // two concurrent sessions

        var revokedCount = await service.RevokeAllAsync(UserUuid);

        Assert.Equal(4, revokedCount); // 2 pairs x 2 tokens
        Assert.Empty(_repository.Rows);
    }

    /// <summary>In-memory stand-in for the auth_tokens table; only the members TokenService uses are real.</summary>
    private sealed class FakeAuthTokenRepository : IAuthTokenRepository
    {
        private readonly Dictionary<string, string> _usersByUuid = [];

        public List<Row> Rows { get; } = [];

        public List<string> RevokeAllUserUuids { get; } = [];

        public int DeleteExpiredCallCount { get; private set; }

        public void AddKnownUser(string userUuid, string username) => _usersByUuid[userUuid] = username;

        public void SeedRow(string userUuid, string username, string tokenHash, string tokenType, string pairUuid, DateTime expiresAt) =>
            Rows.Add(new Row(userUuid, username, tokenHash, tokenType, pairUuid, expiresAt));

        public Row? FindByHash(string tokenHash) => Rows.FirstOrDefault(row => row.TokenHash == tokenHash);

        public Task<bool> AddPairAsync(
            string userUuid,
            string pairUuid,
            string accessTokenHash,
            DateTime accessExpiresAt,
            string refreshTokenHash,
            DateTime refreshExpiresAt,
            CancellationToken cancellationToken = default)
        {
            if (!_usersByUuid.TryGetValue(userUuid, out var username))
                return Task.FromResult(false);

            SeedRow(userUuid, username, accessTokenHash, TokenTypes.Access, pairUuid, accessExpiresAt);
            SeedRow(userUuid, username, refreshTokenHash, TokenTypes.Refresh, pairUuid, refreshExpiresAt);
            return Task.FromResult(true);
        }

        public Task<bool> AddAsync(
            string userUuid,
            string tokenHash,
            string tokenType,
            string pairUuid,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            if (!_usersByUuid.TryGetValue(userUuid, out var username))
                return Task.FromResult(false);

            SeedRow(userUuid, username, tokenHash, tokenType, pairUuid, expiresAt);
            return Task.FromResult(true);
        }

        public Task<AuthTokenLookup?> GetByHashWithUserAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            var row = FindByHash(tokenHash);
            return Task.FromResult(row is null
                ? null
                : new AuthTokenLookup(row.UserUuid, row.Username, row.TokenType, row.PairUuid, row.ExpiresAt, row.RevokedAt));
        }

        public Task<IReadOnlyList<string>> RevokeByPairUuidAsync(string pairUuid, CancellationToken cancellationToken = default)
        {
            var pairRows = Rows.Where(row => row.PairUuid == pairUuid).ToList();
            foreach (var row in pairRows)
                row.RevokedAt ??= DateTime.UtcNow;

            return Task.FromResult<IReadOnlyList<string>>(pairRows.Select(row => row.TokenHash).ToList());
        }

        public Task<bool> RevokeByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            var row = FindByHash(tokenHash);
            if (row is null)
                return Task.FromResult(false);

            row.RevokedAt ??= DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<bool> TryRevokeActiveByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            // Mirrors the conditional `revoked_at IS NULL` UPDATE: only the first caller wins.
            var row = FindByHash(tokenHash);
            if (row is null || row.RevokedAt is not null)
                return Task.FromResult(false);

            row.RevokedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<string>> DeleteAllByUserIdAsync(string userUuid, CancellationToken cancellationToken = default)
        {
            RevokeAllUserUuids.Add(userUuid);
            var userRows = Rows.Where(row => row.UserUuid == userUuid).ToList();
            foreach (var row in userRows)
                Rows.Remove(row);

            return Task.FromResult<IReadOnlyList<string>>(userRows.Select(row => row.TokenHash).ToList());
        }

        public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
        {
            DeleteExpiredCallCount++;
            var expiredRows = Rows.Where(row => row.ExpiresAt <= DateTime.UtcNow).ToList();
            foreach (var row in expiredRows)
                Rows.Remove(row);

            return Task.FromResult(expiredRows.Count);
        }

        public Task<IReadOnlyList<string>> GetActiveHashesByUserAsync(string userUuid, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var hashes = Rows
                .Where(row => row.UserUuid == userUuid && row.RevokedAt is null && row.ExpiresAt > now)
                .Select(row => row.TokenHash)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(hashes);
        }

        // Surface not used by TokenService.
        public IQueryable<AuthToken> Query(bool tracking = false, bool includeDeleted = false) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(
            Func<AppDbContext, CancellationToken, Task<TResult>> query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(
            Func<AppDbContext, TransactionContext, Task<TResult>> action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public sealed record Row(string UserUuid, string Username, string TokenHash, string TokenType, string PairUuid, DateTime ExpiresAt)
        {
            public DateTime? RevokedAt { get; set; }
        }
    }
}
