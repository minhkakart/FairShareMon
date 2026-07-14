using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for per-request token validation over a fake whitelist store: only an
/// unexpired ACCESS entry authenticates; refresh tokens can never authenticate as access tokens.
/// </summary>
public class TokenValidatorTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-000000000001";
    private const string Username = "alice";

    private readonly FakeWhitelistStore _store = new();

    private TokenValidator CreateValidator() => new(_store);

    private void AddEntry(string rawToken, string tokenType, DateTime expiresAt) =>
        _store.Entries[TokenHasher.Sha256Hex(rawToken)] =
            new TokenWhitelistEntry(UserUuid, expiresAt, Username, tokenType, "pair-uuid-1");

    [Fact]
    public async Task ValidateAsync_ValidAccessToken_ReturnsAuthenticatedUser()
    {
        AddEntry("raw-access", TokenTypes.Access, DateTime.UtcNow.AddMinutes(30));

        var user = await CreateValidator().ValidateAsync("raw-access");

        Assert.NotNull(user);
        Assert.Equal(UserUuid, user!.Id);
        Assert.Equal(Username, user.Username);
    }

    [Fact]
    public async Task ValidateAsync_LooksUpBySha256HexOfRawToken()
    {
        AddEntry("raw-access", TokenTypes.Access, DateTime.UtcNow.AddMinutes(30));

        await CreateValidator().ValidateAsync("raw-access");

        var requestedHash = Assert.Single(_store.LookedUpHashes);
        Assert.Equal(TokenHasher.Sha256Hex("raw-access"), requestedHash); // never the raw token
    }

    [Fact]
    public async Task ValidateAsync_UnknownToken_ReturnsNull()
    {
        var user = await CreateValidator().ValidateAsync("never-issued");

        Assert.Null(user);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredAccessToken_ReturnsNull()
    {
        AddEntry("raw-access", TokenTypes.Access, DateTime.UtcNow.AddSeconds(-1));

        var user = await CreateValidator().ValidateAsync("raw-access");

        Assert.Null(user);
    }

    [Fact]
    public async Task ValidateAsync_RefreshTokenPresentedAsAccess_ReturnsNull()
    {
        AddEntry("raw-refresh", TokenTypes.Refresh, DateTime.UtcNow.AddDays(30));

        var user = await CreateValidator().ValidateAsync("raw-refresh");

        Assert.Null(user); // a valid, unexpired REFRESH token must never authenticate a request
    }

    private sealed class FakeWhitelistStore : ITokenWhitelistStore
    {
        public Dictionary<string, TokenWhitelistEntry> Entries { get; } = [];

        public List<string> LookedUpHashes { get; } = [];

        public Task AddAsync(string tokenHash, TokenWhitelistEntry entry, CancellationToken cancellationToken = default)
        {
            Entries[tokenHash] = entry;
            return Task.CompletedTask;
        }

        public Task<TokenWhitelistEntry?> LookupAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            LookedUpHashes.Add(tokenHash);
            return Task.FromResult(Entries.TryGetValue(tokenHash, out var entry) ? entry : null);
        }

        public Task RemoveAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            Entries.Remove(tokenHash);
            return Task.CompletedTask;
        }
    }
}
