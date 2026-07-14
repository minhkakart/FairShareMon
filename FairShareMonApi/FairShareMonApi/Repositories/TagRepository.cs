using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="Tag"/> rows. Every read/write is resource-owned: scoped by the owning
/// user's UUID so another user's tags are invisible (an ownership miss yields null/false, never the
/// row). Name comparisons run in the DB against the column's <c>utf8mb4_unicode_ci</c> collation, so
/// they are case- AND accent-insensitive (OQ5). "Unique active name per ledger" and
/// reactivation-on-name-reuse are enforced atomically inside the write transaction (no check-then-act
/// race). Soft-delete uses the built-in <c>BaseRepository.Query</c> filter.
/// </summary>
public interface ITagRepository : IBaseRepository, IQueryRepository<Tag>
{
    /// <summary>The current user's tags, sorted name A-&gt;Z (OQ11). Excludes soft-deleted rows unless <paramref name="includeDeleted"/>.</summary>
    Task<IReadOnlyList<Tag>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default);

    /// <summary>Resource-owned lookup by UUID (includes soft-deleted rows so callers decide). Null on an ownership miss.</summary>
    Task<Tag?> GetByUuidAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default);

    /// <summary>An active tag with this name (case/accent-insensitive), if any. Null otherwise.</summary>
    Task<Tag?> FindActiveByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>A soft-deleted tag with this name (case/accent-insensitive), if any. Null otherwise.</summary>
    Task<Tag?> FindDeletedByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic create-path: active name collision -&gt; <see cref="NameWriteStatus.NameDuplicate"/>; else a
    /// soft-deleted same-name tag is reactivated (clears <c>IsDeleted</c>; nothing else - tags are
    /// name-only); else a new tag is inserted. Unknown user -&gt; <see cref="NameWriteStatus.NotFound"/>.
    /// </summary>
    Task<NameWriteResult<Tag>> CreateAsync(string userUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>Renames a tag with an in-transaction uniqueness re-check excluding self. Miss -&gt; NotFound; active collision -&gt; NameDuplicate.</summary>
    Task<NameWriteResult<Tag>> RenameAsync(string userUuid, string tagUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a tag scoped to the user (idempotent). False on an ownership miss.</summary>
    Task<bool> SoftDeleteAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(ITagRepository))]
public sealed class TagRepository(AppDbContext dbContext) : BaseRepository(dbContext), ITagRepository
{
    public IQueryable<Tag> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<Tag>(tracking, includeDeleted);

    public Task<IReadOnlyList<Tag>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var tags = await Query(includeDeleted: includeDeleted)
                .Where(tag => tag.User.Uuid == userUuid)
                .OrderBy(tag => tag.Name)
                .ToListAsync(ct);
            return (IReadOnlyList<Tag>)tags;
        }, cancellationToken);

    public Task<Tag?> GetByUuidAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query(includeDeleted: true)
            .FirstOrDefaultAsync(tag => tag.Uuid == tagUuid && tag.User.Uuid == userUuid, ct), cancellationToken);

    public Task<Tag?> FindActiveByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .FirstOrDefaultAsync(tag => tag.User.Uuid == userUuid && tag.Name == name, ct), cancellationToken);

    public Task<Tag?> FindDeletedByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query(includeDeleted: true)
            .FirstOrDefaultAsync(tag => tag.User.Uuid == userUuid && tag.IsDeleted && tag.Name == name, ct), cancellationToken);

    public Task<NameWriteResult<Tag>> CreateAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return NameWriteResult<Tag>.NotFound();
            }

            var activeExists = await db.Tags.AsNoTracking()
                .AnyAsync(tag => tag.UserId == userId && !tag.IsDeleted && tag.Name == name, cancellationToken);
            if (activeExists)
            {
                transaction.NoCommit();
                return NameWriteResult<Tag>.NameDuplicate();
            }

            // Reuse of a soft-deleted tag's name revives the old row (relinking history) - §3.4/§5.
            var deleted = await db.Tags
                .FirstOrDefaultAsync(tag => tag.UserId == userId && tag.IsDeleted && tag.Name == name, cancellationToken);
            if (deleted is not null)
            {
                deleted.IsDeleted = false;
                return NameWriteResult<Tag>.Reactivated(deleted);
            }

            var tag = new Tag { UserId = userId.Value, Name = name };
            db.Tags.Add(tag);
            return NameWriteResult<Tag>.Created(tag);
        }, cancellationToken);

    public Task<NameWriteResult<Tag>> RenameAsync(string userUuid, string tagUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var tag = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == tagUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (tag is null)
            {
                transaction.NoCommit();
                return NameWriteResult<Tag>.NotFound();
            }

            var duplicate = await db.Tags.AsNoTracking()
                .AnyAsync(existing => existing.UserId == tag.UserId
                    && !existing.IsDeleted
                    && existing.Id != tag.Id
                    && existing.Name == name, cancellationToken);
            if (duplicate)
            {
                transaction.NoCommit();
                return NameWriteResult<Tag>.NameDuplicate();
            }

            tag.Name = name;
            return NameWriteResult<Tag>.Updated(tag);
        }, cancellationToken);

    public Task<bool> SoftDeleteAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            // includeDeleted: an already-deleted tag is a harmless no-op success, not a miss.
            var tag = await Query(tracking: true, includeDeleted: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == tagUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (tag is null)
            {
                transaction.NoCommit();
                return false;
            }

            tag.IsDeleted = true;
            return true;
        }, cancellationToken);

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
