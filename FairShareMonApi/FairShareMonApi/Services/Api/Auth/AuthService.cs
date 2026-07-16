using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Registration;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Auth;

/// <summary>
/// Business logic for The-ideal.md §3.1 (Tài khoản &amp; phiên đăng nhập): register (Free tier),
/// login (BCrypt verify -> opaque token pair), refresh (full pair rotation + reuse detection),
/// logout (pair revocation), change password (revoke ALL sessions after commit).
/// </summary>
public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<TokenPairResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<TokenPairResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revokes the presenting token's pair. Idempotent - an already-revoked token still logs out successfully.</summary>
    Task LogoutAsync(string rawAccessToken, CancellationToken cancellationToken = default);

    /// <summary>Verifies the current password, stores the new hash, then revokes ALL of the user's tokens.</summary>
    Task ChangePasswordAsync(string userUuid, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IAuthService))]
public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IMapper mapper,
    IEnumerable<IRegistrationBootstrapStep> registrationBootstrapSteps,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<RefreshRequest> refreshValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator) : IAuthService
{
    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);

        var username = request.Username.ToLowerInvariant();
        if (await userRepository.ExistsByUsernameAsync(username, cancellationToken))
            throw new ErrorException(ErrorCodes.UsernameTaken, MessageKeys.Error.UsernameTaken);

        var user = new User
        {
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password)
        };

        // CreateWithBootstrapAsync re-checks uniqueness inside the transaction and absorbs the
        // unique-index race - null means another request took the username first. The bootstrap
        // steps (owner-representative member, and later suggested categories) run in the SAME
        // transaction, so a registration that rolls back leaves neither a user nor a member.
        var created = await userRepository.CreateWithBootstrapAsync(user, RunRegistrationBootstrapAsync, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.UsernameTaken, MessageKeys.Error.UsernameTaken);

        return mapper.Map<UserResponse>(created);
    }

    // Runs every registered bootstrap step inside the user-creation transaction (after user.Id is
    // assigned). Steps only stage rows on the context; the repository owns the commit.
    private async Task RunRegistrationBootstrapAsync(AppDbContext dbContext, User user, CancellationToken cancellationToken)
    {
        foreach (var step in registrationBootstrapSteps)
            await step.RunAsync(dbContext, user, cancellationToken);
    }

    public async Task<TokenPairResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await userRepository.GetByUsernameAsync(request.Username.ToLowerInvariant(), cancellationToken);
        var passwordValid = passwordHasher.Verify(request.Password, user?.PasswordHash ?? passwordHasher.CreateDummyHash());
        if (user is null || !passwordValid)
            throw new ErrorException(ErrorCodes.InvalidCredentials, MessageKeys.Error.InvalidCredentials);

        // A disabled account cannot authenticate (M11, OQ2). Checked AFTER the credential check so a
        // wrong password still reports invalid-credentials (no account-existence leak on bad password).
        if (user.Status == UserStatuses.Disabled)
            throw new ErrorException(ErrorCodes.AccountDisabled, MessageKeys.Error.AccountDisabled);

        var pair = await tokenService.IssueAsync(user.Uuid, user.Username, user.Tier, user.Role, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.InternalError, MessageKeys.Error.InternalError);

        return mapper.Map<TokenPairResponse>(pair);
    }

    public async Task<TokenPairResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);

        var pair = await tokenService.RefreshAsync(request.RefreshToken, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.InvalidRefreshToken, MessageKeys.Error.InvalidRefreshToken);

        return mapper.Map<TokenPairResponse>(pair);
    }

    public Task LogoutAsync(string rawAccessToken, CancellationToken cancellationToken = default) =>
        // Idempotent: revoking an already-revoked/unknown token is still a successful logout.
        tokenService.RevokeAsync(rawAccessToken, cancellationToken);

    public async Task ChangePasswordAsync(string userUuid, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await userRepository.GetByUuidAsync(userUuid, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.Unauthorized, MessageKeys.Error.Unauthorized);

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ErrorException(ErrorCodes.CurrentPasswordIncorrect, MessageKeys.Error.CurrentPasswordIncorrect);

        var updated = await userRepository.UpdatePasswordAsync(userUuid, passwordHasher.Hash(request.NewPassword), cancellationToken);
        if (!updated)
            throw new ErrorException(ErrorCodes.Unauthorized, MessageKeys.Error.Unauthorized);

        // Post-commit side-effect (rules.md): the hash update is committed - now force every
        // logged-in device to re-login (spec §3.1).
        await tokenService.RevokeAllAsync(userUuid, cancellationToken);
    }
}
