using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Repositories;
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
            throw new ErrorException(ErrorCodes.UsernameTaken, "Tên đăng nhập đã tồn tại.");

        var user = new User
        {
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password)
        };

        // CreateAsync re-checks uniqueness inside the transaction and absorbs the unique-index
        // race - null means another request took the username first.
        var created = await userRepository.CreateAsync(user, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.UsernameTaken, "Tên đăng nhập đã tồn tại.");

        return mapper.Map<UserResponse>(created);
    }

    public async Task<TokenPairResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await userRepository.GetByUsernameAsync(request.Username.ToLowerInvariant(), cancellationToken);
        var passwordValid = passwordHasher.Verify(request.Password, user?.PasswordHash ?? passwordHasher.CreateDummyHash());
        if (user is null || !passwordValid)
            throw new ErrorException(ErrorCodes.InvalidCredentials, "Tên đăng nhập hoặc mật khẩu không đúng.");

        var pair = await tokenService.IssueAsync(user.Uuid, user.Username, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.InternalError, "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.");

        return mapper.Map<TokenPairResponse>(pair);
    }

    public async Task<TokenPairResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);

        var pair = await tokenService.RefreshAsync(request.RefreshToken, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.InvalidRefreshToken, "Mã gia hạn phiên không hợp lệ hoặc đã hết hạn.");

        return mapper.Map<TokenPairResponse>(pair);
    }

    public Task LogoutAsync(string rawAccessToken, CancellationToken cancellationToken = default) =>
        // Idempotent: revoking an already-revoked/unknown token is still a successful logout.
        tokenService.RevokeAsync(rawAccessToken, cancellationToken);

    public async Task ChangePasswordAsync(string userUuid, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await userRepository.GetByUuidAsync(userUuid, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ErrorException(ErrorCodes.CurrentPasswordIncorrect, "Mật khẩu hiện tại không đúng.");

        var updated = await userRepository.UpdatePasswordAsync(userUuid, passwordHasher.Hash(request.NewPassword), cancellationToken);
        if (!updated)
            throw new ErrorException(ErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");

        // Post-commit side-effect (rules.md): the hash update is committed - now force every
        // logged-in device to re-login (spec §3.1).
        await tokenService.RevokeAllAsync(userUuid, cancellationToken);
    }
}
