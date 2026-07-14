using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="Member"/> rows. Every read/write is resource-owned: scoped by the
/// owning user's UUID so another user's members are invisible (an ownership miss yields
/// null/false, never the row). Soft-delete uses the built-in <c>BaseRepository.Query</c> filter.
/// </summary>
public interface IMemberRepository : IBaseRepository, IQueryRepository<Member>
{
    /// <summary>The current user's members, sorted owner-rep first then name A-&gt;Z (OQ8). Excludes soft-deleted rows unless <paramref name="includeDeleted"/>.</summary>
    Task<IReadOnlyList<Member>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default);

    /// <summary>Resource-owned lookup by UUID (includes soft-deleted rows so callers decide). Null on an ownership miss.</summary>
    Task<Member?> GetByUuidAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default);

    /// <summary>Inserts a member for the user (user_id resolved from the UUID). Null when the user is unknown.</summary>
    Task<Member?> CreateAsync(string userUuid, Member member, CancellationToken cancellationToken = default);

    /// <summary>Renames a member scoped to the user. Returns the updated member; null on an ownership miss.</summary>
    Task<Member?> RenameAsync(string userUuid, string memberUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a member scoped to the user (idempotent). False on an ownership miss.</summary>
    Task<bool> SoftDeleteAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default);

    /// <summary>True when the user already has an active owner-representative member (idempotency guard).</summary>
    Task<bool> HasOwnerRepresentativeAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>UUIDs of users lacking an active owner-representative member (for the backfill, OQ2).</summary>
    Task<IReadOnlyList<string>> GetUserUuidsWithoutOwnerRepresentativeAsync(CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IMemberRepository))]
public sealed class MemberRepository(AppDbContext dbContext) : BaseRepository(dbContext), IMemberRepository
{
    public IQueryable<Member> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<Member>(tracking, includeDeleted);

    public Task<IReadOnlyList<Member>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var members = await Query(includeDeleted: includeDeleted)
                .Where(member => member.User.Uuid == userUuid)
                .OrderByDescending(member => member.IsOwnerRepresentative)
                .ThenBy(member => member.Name)
                .ToListAsync(ct);
            return (IReadOnlyList<Member>)members;
        }, cancellationToken);

    public Task<Member?> GetByUuidAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query(includeDeleted: true)
            .FirstOrDefaultAsync(member => member.Uuid == memberUuid && member.User.Uuid == userUuid, ct), cancellationToken);

    public Task<Member?> CreateAsync(string userUuid, Member member, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return null;
            }

            member.UserId = userId.Value;
            db.Members.Add(member);
            return member;
        }, cancellationToken);

    public Task<Member?> RenameAsync(string userUuid, string memberUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var member = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == memberUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (member is null)
            {
                transaction.NoCommit();
                return null;
            }

            member.Name = name;
            return member;
        }, cancellationToken);

    public Task<bool> SoftDeleteAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            // includeDeleted: an already-deleted member is a harmless no-op success, not a miss.
            var member = await Query(tracking: true, includeDeleted: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == memberUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (member is null)
            {
                transaction.NoCommit();
                return false;
            }

            member.IsDeleted = true;
            return true;
        }, cancellationToken);

    public Task<bool> HasOwnerRepresentativeAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .AnyAsync(member => member.User.Uuid == userUuid && member.IsOwnerRepresentative, ct), cancellationToken);

    public Task<IReadOnlyList<string>> GetUserUuidsWithoutOwnerRepresentativeAsync(CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (db, ct) =>
        {
            var uuids = await db.Users.AsNoTracking()
                .Where(user => !db.Members
                    .Any(member => member.UserId == user.Id && member.IsOwnerRepresentative && !member.IsDeleted))
                .Select(user => user.Uuid)
                .ToListAsync(ct);
            return (IReadOnlyList<string>)uuids;
        }, cancellationToken);

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
