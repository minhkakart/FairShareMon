using AutoMapper;
using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Repositories.Admin;
using FairShareMonApi.Services.Api.Admin;
using FairShareMonApi.Validators.Admin;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="AdminUserService"/> over fake repositories + token service and
/// a real AutoMapper/validators. Proves the OQ10 destructive-action guards (self -> 14001; any admin
/// target -> 14002, which subsumes the last-admin case), that grant/revoke record the right append-only
/// row + flip tier + bust the target's cached state (OQ3a/OQ6), that disable/reset-password use the
/// immediate <c>RevokeAllAsync</c> kill-switch, that reset-password returns the temp password once, and
/// that an unknown target uuid -> 14000. Promotion (-> ADMIN) is unguarded; demotion (-> USER) is
/// guarded like any destructive action.
/// </summary>
public class AdminUserServiceTests
{
    private const string AdminUuid = "0198a5c2-0000-7000-8000-0000000a0001";
    private const string OtherAdminUuid = "0198a5c2-0000-7000-8000-0000000a0002";
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000b0001";

    private readonly FakeUserRepository _users = new();
    private readonly FakeTierGrantRepository _grants = new();
    private readonly FakeTokenService _tokens = new();
    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<AdminProfile>()).CreateMapper();

    private readonly AuthenticatedUser _actingAdmin;

    public AdminUserServiceTests()
    {
        _users.Add(new User { Uuid = AdminUuid, Username = "admin", PasswordHash = "h", Role = UserRoles.Admin });
        _actingAdmin = new AuthenticatedUser { Id = AdminUuid, Username = "admin", Role = UserRoles.Admin };
    }

    private readonly PasswordHasher _passwordHasher = new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:BcryptWorkFactor"] = "4" }).Build());

    private AdminUserService CreateService() => new(
        _users, _grants, _passwordHasher, _tokens, _mapper,
        new AdminUserListRequestValidator(),
        new GrantTierRequestValidator(),
        new RevokeTierRequestValidator(),
        new ResetPasswordRequestValidator(),
        new SetRoleRequestValidator(),
        NullLogger<AdminUserService>.Instance);

    private User AddUser(string uuid = UserUuid, string role = UserRoles.User, string tier = UserTiers.Free, string status = UserStatuses.Active)
    {
        var user = new User { Uuid = uuid, Username = "u_" + uuid[^4..], PasswordHash = "old-hash", Role = role, Tier = tier, Status = status };
        _users.Add(user);
        return user;
    }

    // ---- Grant / revoke -----------------------------------------------------------------------------

    [Fact]
    public async Task GrantTierAsync_NonAdminTarget_RecordsGrantRow_FlipsPremium_BustsCache()
    {
        var target = AddUser();

        var row = await CreateService().GrantTierAsync(_actingAdmin, target.Uuid, new GrantTierRequest { Amount = 199_000m, Reference = "TT1" });

        var recorded = Assert.Single(_grants.Recorded);
        Assert.Equal(TierGrantActions.Grant, recorded.Grant.Action);
        Assert.Equal(UserTiers.Premium, recorded.Grant.Tier);
        Assert.Equal(UserTiers.Premium, recorded.NewTier);
        Assert.Equal(199_000m, recorded.Grant.Amount);
        Assert.Equal("admin", recorded.Grant.GrantedByUsername);
        Assert.Equal(target.Username, recorded.Grant.UserUsername);
        Assert.Equal(TierGrantActions.Grant, row.Action);
        Assert.Contains(target.Uuid, _tokens.CacheBustCalls); // cache-bust so the upgrade applies on next request
        Assert.Empty(_tokens.RevokeAllCalls);                 // a grant never logs the user out
    }

    [Fact]
    public async Task GrantTierAsync_DefaultsCurrencyToVnd_WhenOmitted()
    {
        var target = AddUser();

        await CreateService().GrantTierAsync(_actingAdmin, target.Uuid, new GrantTierRequest { Amount = 0m });

        Assert.Equal("VND", Assert.Single(_grants.Recorded).Grant.Currency);
    }

    [Fact]
    public async Task GrantTierAsync_NegativeAmount_ThrowsValidation()
    {
        var target = AddUser();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().GrantTierAsync(_actingAdmin, target.Uuid, new GrantTierRequest { Amount = -1m }));

        Assert.Empty(_grants.Recorded);
    }

    [Fact]
    public async Task GrantTierAsync_UnknownUser_Throws14000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GrantTierAsync(_actingAdmin, "no-such-uuid", new GrantTierRequest { Amount = 1m }));

        Assert.Equal(ErrorCodes.AdminUserNotFound, exception.Code);
        Assert.Empty(_grants.Recorded);
    }

    [Fact]
    public async Task RevokeTierAsync_NonAdminTarget_RecordsRevokeRow_FlipsFree_BustsCache()
    {
        var target = AddUser(tier: UserTiers.Premium);

        var row = await CreateService().RevokeTierAsync(_actingAdmin, target.Uuid, new RevokeTierRequest { Note = "hết hạn" });

        var recorded = Assert.Single(_grants.Recorded);
        Assert.Equal(TierGrantActions.Revoke, recorded.Grant.Action);
        Assert.Equal(UserTiers.Free, recorded.NewTier);
        Assert.Equal(0m, recorded.Grant.Amount); // a revoke is never revenue
        Assert.Equal(TierGrantActions.Revoke, row.Action);
        Assert.Contains(target.Uuid, _tokens.CacheBustCalls);
    }

    // ---- Disable / enable ---------------------------------------------------------------------------

    [Fact]
    public async Task DisableAsync_NonAdminTarget_SetsDisabled_AndRevokesAllTokens()
    {
        var target = AddUser();

        await CreateService().DisableAsync(_actingAdmin, target.Uuid);

        Assert.Equal(UserStatuses.Disabled, _users.Get(target.Uuid)!.Status);
        Assert.Contains(target.Uuid, _tokens.RevokeAllCalls); // immediate, permanent logout
    }

    [Fact]
    public async Task DisableAsync_Self_Throws14001()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DisableAsync(_actingAdmin, AdminUuid));

        Assert.Equal(ErrorCodes.AdminCannotTargetSelf, exception.Code);
        Assert.Empty(_tokens.RevokeAllCalls);
    }

    [Fact]
    public async Task DisableAsync_AnotherAdmin_Throws14002()
    {
        AddUser(OtherAdminUuid, role: UserRoles.Admin);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DisableAsync(_actingAdmin, OtherAdminUuid));

        Assert.Equal(ErrorCodes.AdminCannotTargetAdmin, exception.Code);
    }

    [Fact]
    public async Task DisableAsync_UnknownUser_Throws14000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DisableAsync(_actingAdmin, "no-such-uuid"));

        Assert.Equal(ErrorCodes.AdminUserNotFound, exception.Code);
    }

    [Fact]
    public async Task EnableAsync_NonAdminTarget_SetsActive_NoTokenRevoke()
    {
        var target = AddUser(status: UserStatuses.Disabled);

        await CreateService().EnableAsync(_actingAdmin, target.Uuid);

        Assert.Equal(UserStatuses.Active, _users.Get(target.Uuid)!.Status);
        Assert.Empty(_tokens.RevokeAllCalls); // enable restores login; no tokens auto-restored
    }

    // ---- Revoke tokens ------------------------------------------------------------------------------

    [Fact]
    public async Task RevokeTokensAsync_NonAdminTarget_RevokesAll()
    {
        var target = AddUser();

        await CreateService().RevokeTokensAsync(_actingAdmin, target.Uuid);

        Assert.Contains(target.Uuid, _tokens.RevokeAllCalls);
    }

    [Fact]
    public async Task RevokeTokensAsync_Self_Throws14001()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().RevokeTokensAsync(_actingAdmin, AdminUuid));

        Assert.Equal(ErrorCodes.AdminCannotTargetSelf, exception.Code);
    }

    [Fact]
    public async Task RevokeTokensAsync_AnotherAdmin_Throws14002()
    {
        AddUser(OtherAdminUuid, role: UserRoles.Admin);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().RevokeTokensAsync(_actingAdmin, OtherAdminUuid));

        Assert.Equal(ErrorCodes.AdminCannotTargetAdmin, exception.Code);
    }

    // ---- Reset password -----------------------------------------------------------------------------

    [Fact]
    public async Task ResetPasswordAsync_NonAdminTarget_ReturnsTempPassword_Rehashes_RevokesAll()
    {
        var target = AddUser();
        var oldHash = _users.Get(target.Uuid)!.PasswordHash;

        var response = await CreateService().ResetPasswordAsync(_actingAdmin, target.Uuid, new ResetPasswordRequest { NewPassword = "brand-new-8+" });

        Assert.Equal("brand-new-8+", response.Password);           // temp password returned once
        Assert.Equal(target.Username, response.Username);
        Assert.NotEqual(oldHash, _users.Get(target.Uuid)!.PasswordHash); // stored a fresh hash...
        Assert.NotEqual("brand-new-8+", _users.Get(target.Uuid)!.PasswordHash); // ...not the plaintext
        Assert.Contains(target.Uuid, _tokens.RevokeAllCalls);      // force re-login everywhere
    }

    [Fact]
    public async Task ResetPasswordAsync_Self_Throws14001()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().ResetPasswordAsync(_actingAdmin, AdminUuid, new ResetPasswordRequest { NewPassword = "brand-new-8+" }));

        Assert.Equal(ErrorCodes.AdminCannotTargetSelf, exception.Code);
    }

    [Fact]
    public async Task ResetPasswordAsync_AnotherAdmin_Throws14002()
    {
        AddUser(OtherAdminUuid, role: UserRoles.Admin);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().ResetPasswordAsync(_actingAdmin, OtherAdminUuid, new ResetPasswordRequest { NewPassword = "brand-new-8+" }));

        Assert.Equal(ErrorCodes.AdminCannotTargetAdmin, exception.Code);
    }

    [Fact]
    public async Task ResetPasswordAsync_TooShort_ThrowsValidation()
    {
        var target = AddUser();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().ResetPasswordAsync(_actingAdmin, target.Uuid, new ResetPasswordRequest { NewPassword = "short" }));
    }

    // ---- Role promote / demote ----------------------------------------------------------------------

    [Fact]
    public async Task SetRoleAsync_PromoteUser_IsAllowed_AndBustsCache()
    {
        var target = AddUser();

        await CreateService().SetRoleAsync(_actingAdmin, target.Uuid, new SetRoleRequest { Role = UserRoles.Admin });

        Assert.Equal(UserRoles.Admin, _users.Get(target.Uuid)!.Role);
        Assert.Contains(target.Uuid, _tokens.CacheBustCalls);
    }

    [Fact]
    public async Task SetRoleAsync_DemoteSelf_Throws14001()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().SetRoleAsync(_actingAdmin, AdminUuid, new SetRoleRequest { Role = UserRoles.User }));

        Assert.Equal(ErrorCodes.AdminCannotTargetSelf, exception.Code);
        Assert.Equal(UserRoles.Admin, _users.Get(AdminUuid)!.Role); // unchanged
    }

    [Fact]
    public async Task SetRoleAsync_DemoteAnotherAdmin_Throws14002()
    {
        AddUser(OtherAdminUuid, role: UserRoles.Admin);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().SetRoleAsync(_actingAdmin, OtherAdminUuid, new SetRoleRequest { Role = UserRoles.User }));

        Assert.Equal(ErrorCodes.AdminCannotTargetAdmin, exception.Code);
    }

    [Fact]
    public async Task SetRoleAsync_UnknownRole_ThrowsValidation()
    {
        var target = AddUser();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().SetRoleAsync(_actingAdmin, target.Uuid, new SetRoleRequest { Role = "ROOT" }));
    }

    // ---- Fakes --------------------------------------------------------------------------------------

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _byUuid = [];
        private ulong _nextId = 1;

        public void Add(User user)
        {
            if (user.Id == 0) user.Id = _nextId++;
            _byUuid[user.Uuid] = user;
        }

        public User? Get(string uuid) => _byUuid.GetValueOrDefault(uuid);

        public Task<User?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byUuid.GetValueOrDefault(uuid));

        public Task<bool> SetStatusAsync(string uuid, string status, CancellationToken cancellationToken = default) =>
            MutateAsync(uuid, user => user.Status = status);

        public Task<bool> SetRoleAsync(string uuid, string role, CancellationToken cancellationToken = default) =>
            MutateAsync(uuid, user => user.Role = role);

        public Task<bool> SetTierAsync(string uuid, string tier, CancellationToken cancellationToken = default) =>
            MutateAsync(uuid, user => user.Tier = tier);

        public Task<bool> UpdatePasswordAsync(string uuid, string passwordHash, CancellationToken cancellationToken = default) =>
            MutateAsync(uuid, user => user.PasswordHash = passwordHash);

        public Task<int> CountByRoleAsync(string role, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byUuid.Values.Count(user => user.Role == role));

        private Task<bool> MutateAsync(string uuid, Action<User> mutate)
        {
            if (!_byUuid.TryGetValue(uuid, out var user))
                return Task.FromResult(false);
            mutate(user);
            return Task.FromResult(true);
        }

        public Task<(IReadOnlyList<AdminUserAccount> Rows, int Total)> ListForAdminAsync(AdminUserQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> CreateAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> CreateWithBootstrapAsync(User user, Func<AppDbContext, User, CancellationToken, Task> bootstrap, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<User> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTierGrantRepository : ITierGrantRepository
    {
        public List<(string Uuid, string NewTier, TierGrant Grant)> Recorded { get; } = [];

        public Task<TierGrant?> RecordAsync(string userUuid, string newTier, TierGrant grant, CancellationToken cancellationToken = default)
        {
            Recorded.Add((userUuid, newTier, grant));
            return Task.FromResult<TierGrant?>(grant);
        }

        public Task<TierGrant> AddAsync(TierGrant grant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TierGrant>> ListByUserIdAsync(ulong userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TierGrant>>([]);
        public Task<IReadOnlyList<TierGrantSummary>> GetGrantSummariesAsync(IReadOnlyList<ulong> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TierGrantSummary>>([]);
        public Task<RevenueAggregate> GetRevenueAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<TierGrant> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTokenService : ITokenService
    {
        public List<string> CacheBustCalls { get; } = [];
        public List<string> RevokeAllCalls { get; } = [];

        public Task RefreshCachedStateAsync(string userUuid, CancellationToken cancellationToken = default)
        {
            CacheBustCalls.Add(userUuid);
            return Task.CompletedTask;
        }

        public Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
        {
            RevokeAllCalls.Add(userId);
            return Task.FromResult(0);
        }

        public Task<TokenPair?> IssueAsync(string userId, string username, string tier = UserTiers.Free, string role = UserRoles.User, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(string rawToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
