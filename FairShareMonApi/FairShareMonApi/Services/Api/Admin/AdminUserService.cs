using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Admin;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Admin;

/// <summary>
/// Account-level user administration (M11). Acts ONLY on account metadata + tier-grant records - never
/// on any user's ledger data (members/expenses/events/shares/bank accounts), and builds no cross-user
/// ledger query (R10). Grant/revoke flip <c>users.tier</c>, append a <c>tier_grants</c> row, and bust
/// the target's cached token state so the change applies on their next request without a forced logout
/// (OQ3a). Disable + reset-password use the existing <c>RevokeAllAsync</c> kill-switch (immediate cut).
/// Destructive actions (disable/demote/revoke-tokens/reset-password) are guarded: never against self
/// (14001) and never against another admin / down to zero admins (14002) - OQ10.
/// </summary>
public interface IAdminUserService
{
    Task<PagedResult<AdminUserRow>> ListAsync(AdminUserListRequest request, CancellationToken cancellationToken = default);

    Task<AdminUserDetailResponse> GetAsync(string uuid, CancellationToken cancellationToken = default);

    Task<TierGrantRow> GrantTierAsync(AuthenticatedUser actingAdmin, string uuid, GrantTierRequest request, CancellationToken cancellationToken = default);

    Task<TierGrantRow> RevokeTierAsync(AuthenticatedUser actingAdmin, string uuid, RevokeTierRequest request, CancellationToken cancellationToken = default);

    Task DisableAsync(AuthenticatedUser actingAdmin, string uuid, CancellationToken cancellationToken = default);

    Task EnableAsync(AuthenticatedUser actingAdmin, string uuid, CancellationToken cancellationToken = default);

    Task RevokeTokensAsync(AuthenticatedUser actingAdmin, string uuid, CancellationToken cancellationToken = default);

    Task<ResetPasswordResponse> ResetPasswordAsync(AuthenticatedUser actingAdmin, string uuid, ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task SetRoleAsync(AuthenticatedUser actingAdmin, string uuid, SetRoleRequest request, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IAdminUserService))]
public sealed class AdminUserService(
    IUserRepository userRepository,
    ITierGrantRepository tierGrantRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IMapper mapper,
    IValidator<AdminUserListRequest> listValidator,
    IValidator<GrantTierRequest> grantValidator,
    IValidator<RevokeTierRequest> revokeValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator,
    IValidator<SetRoleRequest> setRoleValidator,
    ILogger<AdminUserService> logger) : IAdminUserService
{
    private const string DefaultCurrency = "VND";

    public async Task<PagedResult<AdminUserRow>> ListAsync(AdminUserListRequest request, CancellationToken cancellationToken = default)
    {
        await listValidator.ValidateAndThrowAsync(request, cancellationToken);

        var query = new AdminUserQuery(
            request.Tier,
            request.Status,
            request.Role,
            request.Search,
            request.Page,
            request.PageSize,
            request.Sort,
            request.Direction == "desc");

        var (accounts, total) = await userRepository.ListForAdminAsync(query, cancellationToken);

        var summaries = await tierGrantRepository.GetGrantSummariesAsync(
            accounts.Select(account => account.Id).ToList(), cancellationToken);
        var summaryByUserId = summaries.ToDictionary(summary => summary.UserId);

        var rows = accounts.Select(account =>
        {
            var row = mapper.Map<AdminUserRow>(account);
            if (summaryByUserId.TryGetValue(account.Id, out var summary))
            {
                row.GrantCount = summary.GrantCount;
                row.LastGrantAt = summary.LastGrantAt;
            }

            return row;
        }).ToList();

        return new PagedResult<AdminUserRow>
        {
            Items = rows,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = total
        };
    }

    public async Task<AdminUserDetailResponse> GetAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();

        var grants = await tierGrantRepository.ListByUserIdAsync(user.Id, cancellationToken);

        var response = mapper.Map<AdminUserDetailResponse>(user);
        response.Grants = mapper.Map<IReadOnlyList<TierGrantRow>>(grants);
        return response;
    }

    public async Task<TierGrantRow> GrantTierAsync(AuthenticatedUser actingAdmin, string uuid, GrantTierRequest request, CancellationToken cancellationToken = default)
    {
        await grantValidator.ValidateAndThrowAsync(request, cancellationToken);

        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();
        var actingUserId = await ResolveActingUserIdAsync(actingAdmin, cancellationToken);

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? DefaultCurrency : request.Currency.Trim();
        var grant = new TierGrant
        {
            UserId = target.Id,
            UserUsername = target.Username,
            Tier = UserTiers.Premium,
            Action = TierGrantActions.Grant,
            Amount = request.Amount,
            Currency = currency,
            Reference = request.Reference,
            Note = request.Note,
            GrantedByUserId = actingUserId,
            GrantedByUsername = actingAdmin.Username
        };

        var recorded = await tierGrantRepository.RecordAsync(uuid, UserTiers.Premium, grant, cancellationToken)
            ?? throw UserNotFound();

        // Post-commit (rules.md): bust the target's Redis token cache so the next request reads live PREMIUM (OQ3a).
        await tokenService.RefreshCachedStateAsync(uuid, cancellationToken);

        logger.LogInformation("Admin {Admin} granted PREMIUM to user {User} (amount {Amount} {Currency}).",
            actingAdmin.Username, target.Username, request.Amount, currency);

        return mapper.Map<TierGrantRow>(recorded);
    }

    public async Task<TierGrantRow> RevokeTierAsync(AuthenticatedUser actingAdmin, string uuid, RevokeTierRequest request, CancellationToken cancellationToken = default)
    {
        await revokeValidator.ValidateAndThrowAsync(request, cancellationToken);

        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();
        var actingUserId = await ResolveActingUserIdAsync(actingAdmin, cancellationToken);

        var grant = new TierGrant
        {
            UserId = target.Id,
            UserUsername = target.Username,
            Tier = UserTiers.Free,
            Action = TierGrantActions.Revoke,
            Amount = 0m,
            Currency = DefaultCurrency,
            Reference = null,
            Note = request.Note,
            GrantedByUserId = actingUserId,
            GrantedByUsername = actingAdmin.Username
        };

        var recorded = await tierGrantRepository.RecordAsync(uuid, UserTiers.Free, grant, cancellationToken)
            ?? throw UserNotFound();

        await tokenService.RefreshCachedStateAsync(uuid, cancellationToken);

        logger.LogInformation("Admin {Admin} revoked PREMIUM from user {User}.", actingAdmin.Username, target.Username);

        return mapper.Map<TierGrantRow>(recorded);
    }

    public async Task DisableAsync(AuthenticatedUser actingAdmin, string uuid, CancellationToken cancellationToken = default)
    {
        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();
        EnsureDestructiveAllowed(actingAdmin, target);

        var updated = await userRepository.SetStatusAsync(uuid, UserStatuses.Disabled, cancellationToken);
        if (!updated)
            throw UserNotFound();

        // Post-commit: immediate, permanent logout - a disabled account cannot hold a valid token (OQ2).
        await tokenService.RevokeAllAsync(uuid, cancellationToken);

        logger.LogInformation("Admin {Admin} disabled user {User}.", actingAdmin.Username, target.Username);
    }

    public async Task EnableAsync(AuthenticatedUser actingAdmin, string uuid, CancellationToken cancellationToken = default)
    {
        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();

        var updated = await userRepository.SetStatusAsync(uuid, UserStatuses.Active, cancellationToken);
        if (!updated)
            throw UserNotFound();

        logger.LogInformation("Admin {Admin} enabled user {User}.", actingAdmin.Username, target.Username);
    }

    public async Task RevokeTokensAsync(AuthenticatedUser actingAdmin, string uuid, CancellationToken cancellationToken = default)
    {
        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();
        EnsureDestructiveAllowed(actingAdmin, target);

        await tokenService.RevokeAllAsync(uuid, cancellationToken);

        logger.LogInformation("Admin {Admin} revoked all tokens of user {User}.", actingAdmin.Username, target.Username);
    }

    public async Task<ResetPasswordResponse> ResetPasswordAsync(AuthenticatedUser actingAdmin, string uuid, ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        await resetPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);

        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();
        EnsureDestructiveAllowed(actingAdmin, target);

        var updated = await userRepository.UpdatePasswordAsync(uuid, passwordHasher.Hash(request.NewPassword), cancellationToken);
        if (!updated)
            throw UserNotFound();

        // Post-commit: force re-login everywhere, mirroring ChangePasswordAsync's kill-switch.
        await tokenService.RevokeAllAsync(uuid, cancellationToken);

        // Never log the password (OQ8); it is returned to the admin exactly once to relay out-of-band.
        logger.LogInformation("Admin {Admin} reset the password of user {User}.", actingAdmin.Username, target.Username);

        return new ResetPasswordResponse
        {
            Username = target.Username,
            Password = request.NewPassword
        };
    }

    public async Task SetRoleAsync(AuthenticatedUser actingAdmin, string uuid, SetRoleRequest request, CancellationToken cancellationToken = default)
    {
        await setRoleValidator.ValidateAndThrowAsync(request, cancellationToken);

        var target = await userRepository.GetByUuidAsync(uuid, cancellationToken) ?? throw UserNotFound();

        // Demotion (-> USER) is a destructive action: never on self (14001), never on another admin /
        // down to zero admins (14002). Promotion (-> ADMIN) is always allowed.
        if (request.Role == UserRoles.User)
            EnsureDestructiveAllowed(actingAdmin, target);

        var updated = await userRepository.SetRoleAsync(uuid, request.Role, cancellationToken);
        if (!updated)
            throw UserNotFound();

        // Post-commit: bust the cache so the new role applies on the next request, no forced logout (OQ3a).
        await tokenService.RefreshCachedStateAsync(uuid, cancellationToken);

        logger.LogInformation("Admin {Admin} set role {Role} on user {User}.", actingAdmin.Username, request.Role, target.Username);
    }

    private async Task<ulong> ResolveActingUserIdAsync(AuthenticatedUser actingAdmin, CancellationToken cancellationToken)
    {
        var actingUser = await userRepository.GetByUuidAsync(actingAdmin.Id, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
        return actingUser.Id;
    }

    // Self -> 14001; another admin (which also covers "would leave zero admins") -> 14002 (OQ10).
    private static void EnsureDestructiveAllowed(AuthenticatedUser actingAdmin, User target)
    {
        if (string.Equals(target.Uuid, actingAdmin.Id, StringComparison.Ordinal))
            throw new ErrorException(ErrorCodes.AdminCannotTargetSelf,
                "Bạn không thể thực hiện thao tác này với chính tài khoản admin của mình.");

        if (target.Role == UserRoles.Admin)
            throw new ErrorException(ErrorCodes.AdminCannotTargetAdmin,
                "Không thể vô hiệu hóa/hạ quyền một tài khoản admin khác, hoặc thao tác này sẽ khiến hệ thống không còn admin nào.");
    }

    private static ErrorException UserNotFound() =>
        new(ErrorCodes.AdminUserNotFound, "Không tìm thấy người dùng.");
}
