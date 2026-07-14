using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Shared base for the M11 admin repository/seeder integration tests against the real MariaDB
/// (skippable). Extends <see cref="AuthDbTestBase"/> with seed helpers for users carrying a specific
/// role/tier/status and for append-only <c>tier_grants</c> rows.
///
/// Cleanup: <c>tier_grants</c> has NO cascade FK to <c>users</c> (immutable trail, OQ5), so the base
/// user-cascade would leave grant rows behind. This base sweeps <c>tier_grants</c> by the prefix's
/// seeded user ids (as target AND as granting admin) BEFORE the base deletes the users, so no row can
/// survive a run and the real DB is never left dirty.
/// </summary>
public abstract class AdminDbTestBase(DatabaseFixture fixture) : AuthDbTestBase(fixture)
{
    protected async Task<User> SeedUserAsync(
        string role = UserRoles.User,
        string tier = UserTiers.Free,
        string status = UserStatuses.Active,
        string? username = null,
        DateTime? createdAt = null)
    {
        await using var context = CreateContext();
        var user = new User
        {
            Username = username ?? NewUsername(),
            PasswordHash = "seeded-hash-not-a-password",
            Role = role,
            Tier = tier,
            Status = status
        };
        if (createdAt.HasValue)
            user.CreatedAt = createdAt.Value;
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    protected async Task<TierGrant> SeedGrantAsync(
        User target,
        User grantedBy,
        string action,
        decimal amount,
        DateTime createdAt,
        string? reference = null,
        string tier = UserTiers.Premium,
        string currency = "VND")
    {
        await using var context = CreateContext();
        var grant = new TierGrant
        {
            UserId = target.Id,
            UserUsername = target.Username,
            Tier = tier,
            Action = action,
            Amount = amount,
            Currency = currency,
            Reference = reference,
            GrantedByUserId = grantedBy.Id,
            GrantedByUsername = grantedBy.Username,
            CreatedAt = createdAt
        };
        context.TierGrants.Add(grant);
        await context.SaveChangesAsync();
        return grant;
    }

    protected async Task<User?> ReloadUserAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Uuid == uuid);
    }

    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            await using var context = CreateContext();
            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();

            // tier_grants have NO cascade FK (OQ5) - sweep by the prefix's user ids (target or admin) first.
            await context.TierGrants
                .Where(grant => userIds.Contains(grant.UserId) || userIds.Contains(grant.GrantedByUserId))
                .ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
