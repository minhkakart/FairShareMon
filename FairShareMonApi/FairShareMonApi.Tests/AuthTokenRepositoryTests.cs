using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the <c>auth_tokens</c> whitelist repository against the real MariaDB
/// (skippable): pair insertion, the joined lookup that deliberately returns revoked rows (reuse
/// detection needs them), pair-scoped soft-revocation, the hard-delete kill switch, and the
/// opportunistic expired purge.
/// </summary>
[Collection("AuthIntegration")]
public class AuthTokenRepositoryTests(DatabaseFixture fixture) : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private AuthTokenRepository CreateRepository() => new(CreateContext());

    private static string NewHash() => TokenHasher.Sha256Hex(Uuid.NewV7());

    private async Task SeedTokenAsync(ulong userId, string tokenHash, string tokenType, string pairUuid, DateTime expiresAt, DateTime? revokedAt = null)
    {
        await using var context = CreateContext();
        context.AuthTokens.Add(new AuthToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            TokenType = tokenType,
            PairUuid = pairUuid,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt
        });
        await context.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task AddPairAsync_KnownUser_InsertsAccessAndRefreshRowsSharingThePairUuid()
    {
        var user = await SeedUserAsync();
        var pairUuid = Uuid.NewV7();
        var accessHash = NewHash();
        var refreshHash = NewHash();

        var added = await CreateRepository().AddPairAsync(
            user.Uuid, pairUuid, accessHash, DateTime.UtcNow.AddMinutes(30), refreshHash, DateTime.UtcNow.AddDays(30));

        Assert.True(added);
        await using var context = CreateContext();
        var rows = await context.AuthTokens.AsNoTracking().Where(token => token.PairUuid == pairUuid).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.TokenHash == accessHash && row.TokenType == TokenTypes.Access);
        Assert.Single(rows, row => row.TokenHash == refreshHash && row.TokenType == TokenTypes.Refresh);
        Assert.All(rows, row => Assert.Null(row.RevokedAt));
    }

    [SkippableFact]
    public async Task AddPairAsync_UnknownUser_ReturnsFalseAndInsertsNothing()
    {
        var pairUuid = Uuid.NewV7();

        var added = await CreateRepository().AddPairAsync(
            "00000000-0000-7000-8000-000000000000", pairUuid, NewHash(), DateTime.UtcNow.AddMinutes(30), NewHash(), DateTime.UtcNow.AddDays(30));

        Assert.False(added);
        await using var context = CreateContext();
        Assert.Equal(0, await context.AuthTokens.CountAsync(token => token.PairUuid == pairUuid));
    }

    [SkippableFact]
    public async Task AddAsync_SingleRow_PersistsAndUnknownUserReturnsFalse()
    {
        var user = await SeedUserAsync();
        var tokenHash = NewHash();
        var repository = CreateRepository();

        var added = await repository.AddAsync(user.Uuid, tokenHash, TokenTypes.Access, Uuid.NewV7(), DateTime.UtcNow.AddMinutes(30));
        var addedForGhost = await repository.AddAsync(
            "00000000-0000-7000-8000-000000000000", NewHash(), TokenTypes.Access, Uuid.NewV7(), DateTime.UtcNow.AddMinutes(30));

        Assert.True(added);
        Assert.False(addedForGhost);
        await using var context = CreateContext();
        Assert.Equal(1, await context.AuthTokens.CountAsync(token => token.TokenHash == tokenHash));
    }

    [SkippableFact]
    public async Task GetByHashWithUserAsync_JoinsUsernameAndReturnsRevokedRowsToo()
    {
        var user = await SeedUserAsync();
        var tokenHash = NewHash();
        var pairUuid = Uuid.NewV7();
        var revokedAt = DateTime.UtcNow.AddMinutes(-2);
        await SeedTokenAsync(user.Id, tokenHash, TokenTypes.Refresh, pairUuid, DateTime.UtcNow.AddDays(30), revokedAt);

        var lookup = await CreateRepository().GetByHashWithUserAsync(tokenHash);

        Assert.NotNull(lookup);
        Assert.Equal(user.Uuid, lookup!.UserUuid);
        Assert.Equal(user.Username, lookup.Username); // joined - the caller needs no second query
        Assert.Equal(TokenTypes.Refresh, lookup.TokenType);
        Assert.Equal(pairUuid, lookup.PairUuid);
        Assert.NotNull(lookup.RevokedAt); // revoked rows ARE returned: reuse detection distinguishes revoked from unknown
    }

    [SkippableFact]
    public async Task GetByHashWithUserAsync_UnknownHash_ReturnsNull()
    {
        var lookup = await CreateRepository().GetByHashWithUserAsync(NewHash());

        Assert.Null(lookup);
    }

    [SkippableFact]
    public async Task RevokeByPairUuidAsync_SoftRevokesBothRowsAndReturnsBothHashes()
    {
        var user = await SeedUserAsync();
        var pairUuid = Uuid.NewV7();
        var accessHash = NewHash();
        var refreshHash = NewHash();
        await SeedTokenAsync(user.Id, accessHash, TokenTypes.Access, pairUuid, DateTime.UtcNow.AddMinutes(30));
        await SeedTokenAsync(user.Id, refreshHash, TokenTypes.Refresh, pairUuid, DateTime.UtcNow.AddDays(30));

        var revokedHashes = await CreateRepository().RevokeByPairUuidAsync(pairUuid);

        Assert.Equal(2, revokedHashes.Count);
        Assert.Contains(accessHash, revokedHashes);
        Assert.Contains(refreshHash, revokedHashes);
        await using var context = CreateContext();
        var rows = await context.AuthTokens.AsNoTracking().Where(token => token.PairUuid == pairUuid).ToListAsync();
        Assert.Equal(2, rows.Count); // soft-revoke: rows REMAIN (attributable for reuse detection)
        Assert.All(rows, row => Assert.NotNull(row.RevokedAt));
    }

    [SkippableFact]
    public async Task RevokeByPairUuidAsync_AlreadyRevokedRow_KeepsOriginalRevokedAt()
    {
        var user = await SeedUserAsync();
        var pairUuid = Uuid.NewV7();
        var originalRevokedAt = DateTime.UtcNow.AddMinutes(-10);
        await SeedTokenAsync(user.Id, NewHash(), TokenTypes.Refresh, pairUuid, DateTime.UtcNow.AddDays(30), originalRevokedAt);

        await CreateRepository().RevokeByPairUuidAsync(pairUuid);

        await using var context = CreateContext();
        var row = await context.AuthTokens.AsNoTracking().SingleAsync(token => token.PairUuid == pairUuid);
        Assert.NotNull(row.RevokedAt);
        Assert.Equal(originalRevokedAt, row.RevokedAt!.Value, TimeSpan.FromSeconds(1)); // first revocation timestamp preserved
    }

    [SkippableFact]
    public async Task DeleteAllByUserIdAsync_HardDeletesEveryRowAndReturnsTheirHashes()
    {
        var user = await SeedUserAsync();
        var hashes = new[] { NewHash(), NewHash(), NewHash() };
        await SeedTokenAsync(user.Id, hashes[0], TokenTypes.Access, Uuid.NewV7(), DateTime.UtcNow.AddMinutes(30));
        await SeedTokenAsync(user.Id, hashes[1], TokenTypes.Refresh, Uuid.NewV7(), DateTime.UtcNow.AddDays(30));
        await SeedTokenAsync(user.Id, hashes[2], TokenTypes.Refresh, Uuid.NewV7(), DateTime.UtcNow.AddDays(30), DateTime.UtcNow); // revoked rows go too

        var deletedHashes = await CreateRepository().DeleteAllByUserIdAsync(user.Uuid);

        Assert.Equal(3, deletedHashes.Count);
        Assert.All(hashes, hash => Assert.Contains(hash, deletedHashes));
        await using var context = CreateContext();
        Assert.Equal(0, await context.AuthTokens.CountAsync(token => token.UserId == user.Id)); // hard delete - kill switch
    }

    [SkippableFact]
    public async Task DeleteExpiredAsync_PurgesExpiredRowsRevokedOrNotAndKeepsLiveOnes()
    {
        var user = await SeedUserAsync();
        var expiredActiveHash = NewHash();
        var expiredRevokedHash = NewHash();
        var liveHash = NewHash();
        await SeedTokenAsync(user.Id, expiredActiveHash, TokenTypes.Access, Uuid.NewV7(), DateTime.UtcNow.AddMinutes(-5));
        await SeedTokenAsync(user.Id, expiredRevokedHash, TokenTypes.Refresh, Uuid.NewV7(), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(-1));
        await SeedTokenAsync(user.Id, liveHash, TokenTypes.Refresh, Uuid.NewV7(), DateTime.UtcNow.AddDays(30));

        var purgedCount = await CreateRepository().DeleteExpiredAsync();

        Assert.True(purgedCount >= 2); // global purge may also sweep leftovers from other sources
        await using var context = CreateContext();
        var remainingHashes = await context.AuthTokens.AsNoTracking()
            .Where(token => token.UserId == user.Id)
            .Select(token => token.TokenHash)
            .ToListAsync();
        Assert.Equal([liveHash], remainingHashes);
    }
}
