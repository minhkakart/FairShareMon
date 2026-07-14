using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Services.Api.Auth;
using FairShareMonApi.Tests.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>AuthService</c> resolved from the application's own DI container
/// (real repositories, BCrypt, TokenService, MariaDB, live-or-degraded Redis - skippable when the
/// DB is unreachable). Business-rule targets: BCrypt-only persistence, lowercase usernames, FREE
/// tier, stable error codes 2000-2003, full pair rotation, the OQ4 reuse-detection revoke-all
/// cascade, and the change-password kill switch.
/// </summary>
[Collection("AuthIntegration")]
public class AuthServiceTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    private const string Password = "mật-khẩu-8+";
    private const string OtherPassword = "mật-khẩu-khác-9";

    private async Task<UserResponse> RegisterAsync(IServiceScope scope, string? username = null, string password = Password) =>
        await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .RegisterAsync(new RegisterRequest { Username = username ?? NewUsername(), Password = password });

    private static Task<TokenPairResponse> LoginAsync(IServiceScope scope, string username, string password = Password) =>
        scope.ServiceProvider.GetRequiredService<IAuthService>()
            .LoginAsync(new LoginRequest { Username = username, Password = password });

    [SkippableFact]
    public async Task RegisterAsync_NewUser_PersistsBcryptHashLowercaseUsernameAndFreeTier()
    {
        using var scope = CreateScope();
        var mixedCaseUsername = NewUsername() + "MiXeD";

        var response = await RegisterAsync(scope, mixedCaseUsername);

        Assert.Equal(mixedCaseUsername.ToLowerInvariant(), response.Username); // stored lowercase (OQ2)
        Assert.Equal(UserTiers.Free, response.Tier);
        Assert.Equal(36, response.Uuid.Length);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await context.Users.AsNoTracking().SingleAsync(user => user.Uuid == response.Uuid);
        Assert.Equal(mixedCaseUsername.ToLowerInvariant(), persisted.Username);
        Assert.StartsWith("$2", persisted.PasswordHash); // BCrypt hash...
        Assert.DoesNotContain(Password, persisted.PasswordHash); // ...never the plaintext
    }

    [SkippableFact]
    public async Task RegisterAsync_DuplicateUsernameAnyCasing_Throws2000AndPersistsNothing()
    {
        using var scope = CreateScope();
        var username = NewUsername();
        await RegisterAsync(scope, username);

        var exception = await Assert.ThrowsAsync<ErrorException>(() => RegisterAsync(scope, username.ToUpperInvariant()));

        Assert.Equal(ErrorCodes.UsernameTaken, exception.Code);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.Users.CountAsync(user => user.Username == username));
    }

    [SkippableFact]
    public async Task RegisterAsync_InvalidPayload_ThrowsValidationException()
    {
        using var scope = CreateScope();

        // Manual validation (no auto-validation): the service itself must reject bad input.
        await Assert.ThrowsAsync<ValidationException>(() => RegisterAsync(scope, username: "a!", password: "short"));
    }

    [SkippableFact]
    public async Task LoginAsync_ValidCredentials_WhitelistsOnlyHashedTokens()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);

        var pair = await LoginAsync(scope, user.Username);

        Assert.Equal(43, pair.AccessToken.Length);
        Assert.Equal(43, pair.RefreshToken.Length);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedHashes = await context.AuthTokens.AsNoTracking()
            .Where(token => token.User.Uuid == user.Uuid)
            .Select(token => token.TokenHash)
            .ToListAsync();
        Assert.Equal(2, storedHashes.Count);
        Assert.Contains(TokenHasher.Sha256Hex(pair.AccessToken), storedHashes);
        Assert.Contains(TokenHasher.Sha256Hex(pair.RefreshToken), storedHashes);
        Assert.DoesNotContain(pair.AccessToken, storedHashes); // raw tokens are never persisted
    }

    [SkippableFact]
    public async Task LoginAsync_WrongPassword_Throws2001()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);

        var exception = await Assert.ThrowsAsync<ErrorException>(() => LoginAsync(scope, user.Username, OtherPassword));

        Assert.Equal(ErrorCodes.InvalidCredentials, exception.Code);
    }

    [SkippableFact]
    public async Task LoginAsync_UnknownUser_Throws2001()
    {
        using var scope = CreateScope();

        var exception = await Assert.ThrowsAsync<ErrorException>(() => LoginAsync(scope, UsernamePrefix + "ghost"));

        Assert.Equal(ErrorCodes.InvalidCredentials, exception.Code); // same code as wrong password - no user enumeration
    }

    [SkippableFact]
    public async Task RefreshAsync_UnknownToken_Throws2002()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            authService.RefreshAsync(new RefreshRequest { RefreshToken = "never-issued-token" }));

        Assert.Equal(ErrorCodes.InvalidRefreshToken, exception.Code);
    }

    [SkippableFact]
    public async Task RefreshAsync_AccessTokenPresented_Throws2002()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var pair = await LoginAsync(scope, user.Username);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            authService.RefreshAsync(new RefreshRequest { RefreshToken = pair.AccessToken }));

        Assert.Equal(ErrorCodes.InvalidRefreshToken, exception.Code);
    }

    [SkippableFact]
    public async Task RefreshAsync_ExpiredRefreshToken_Throws2002()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var pair = await LoginAsync(scope, user.Username);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshHash = TokenHasher.Sha256Hex(pair.RefreshToken);
        await context.AuthTokens
            .Where(token => token.TokenHash == refreshHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAt, DateTime.UtcNow.AddMinutes(-5)));

        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            authService.RefreshAsync(new RefreshRequest { RefreshToken = pair.RefreshToken }));

        Assert.Equal(ErrorCodes.InvalidRefreshToken, exception.Code);
    }

    [SkippableFact]
    public async Task RefreshAsync_ValidToken_RotatesPairAndOldAccessStopsValidating()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var oldPair = await LoginAsync(scope, user.Username);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var newPair = await authService.RefreshAsync(new RefreshRequest { RefreshToken = oldPair.RefreshToken });

        Assert.NotEqual(oldPair.AccessToken, newPair.AccessToken);
        var tokenValidator = scope.ServiceProvider.GetRequiredService<ITokenValidator>();
        Assert.Null(await tokenValidator.ValidateAsync(oldPair.AccessToken)); // full rotation kills the paired access token
        Assert.NotNull(await tokenValidator.ValidateAsync(newPair.AccessToken));

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oldRefreshHash = TokenHasher.Sha256Hex(oldPair.RefreshToken);
        var oldRow = await context.AuthTokens.AsNoTracking().SingleAsync(token => token.TokenHash == oldRefreshHash);
        Assert.NotNull(oldRow.RevokedAt); // soft-revoked, kept for reuse detection
    }

    [SkippableFact]
    public async Task RefreshAsync_ReusedRevokedRefreshToken_Throws2002AndKillsAllSessions()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var stolenPair = await LoginAsync(scope, user.Username);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var rotatedPair = await authService.RefreshAsync(new RefreshRequest { RefreshToken = stolenPair.RefreshToken });

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            authService.RefreshAsync(new RefreshRequest { RefreshToken = stolenPair.RefreshToken })); // theft signal (OQ4)

        Assert.Equal(ErrorCodes.InvalidRefreshToken, exception.Code);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.AuthTokens.CountAsync(token => token.User.Uuid == user.Uuid)); // hard-deleted, all of them
        var tokenValidator = scope.ServiceProvider.GetRequiredService<ITokenValidator>();
        Assert.Null(await tokenValidator.ValidateAsync(rotatedPair.AccessToken)); // even the legitimately rotated session dies
    }

    [SkippableFact]
    public async Task LogoutAsync_UnknownToken_IsIdempotentAndDoesNotThrow()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        await authService.LogoutAsync("never-issued-token"); // already-revoked/unknown still logs out successfully
    }

    [SkippableFact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_Throws2003AndTokensSurvive()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var pair = await LoginAsync(scope, user.Username);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var exception = await Assert.ThrowsAsync<ErrorException>(() => authService.ChangePasswordAsync(
            user.Uuid, new ChangePasswordRequest { CurrentPassword = OtherPassword, NewPassword = "new-password-9" }));

        Assert.Equal(ErrorCodes.CurrentPasswordIncorrect, exception.Code);
        var tokenValidator = scope.ServiceProvider.GetRequiredService<ITokenValidator>();
        Assert.NotNull(await tokenValidator.ValidateAsync(pair.AccessToken)); // failed attempt revokes nothing
    }

    [SkippableFact]
    public async Task ChangePasswordAsync_Success_UpdatesHashAndRevokesEverySession()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var deviceOne = await LoginAsync(scope, user.Username);
        var deviceTwo = await LoginAsync(scope, user.Username);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        await authService.ChangePasswordAsync(
            user.Uuid, new ChangePasswordRequest { CurrentPassword = Password, NewPassword = OtherPassword });

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.AuthTokens.CountAsync(token => token.User.Uuid == user.Uuid)); // kill switch: zero rows left
        var tokenValidator = scope.ServiceProvider.GetRequiredService<ITokenValidator>();
        Assert.Null(await tokenValidator.ValidateAsync(deviceOne.AccessToken));
        Assert.Null(await tokenValidator.ValidateAsync(deviceTwo.AccessToken));

        await Assert.ThrowsAsync<ErrorException>(() => LoginAsync(scope, user.Username, Password)); // old password dead
        var newPair = await LoginAsync(scope, user.Username, OtherPassword); // new password works
        Assert.Equal(43, newPair.AccessToken.Length);
    }

    [SkippableFact]
    public async Task ChangePasswordAsync_SamePasswordAsCurrent_Succeeds()
    {
        using var scope = CreateScope();
        var user = await RegisterAsync(scope);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        // OQ3 decision: reusing the same password is allowed.
        await authService.ChangePasswordAsync(
            user.Uuid, new ChangePasswordRequest { CurrentPassword = Password, NewPassword = Password });

        var pair = await LoginAsync(scope, user.Username, Password);
        Assert.Equal(43, pair.AccessToken.Length);
    }
}
