using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="Category"/> rows. Every read/write is resource-owned: scoped by the
/// owning user's UUID so another user's categories are invisible (an ownership miss yields
/// null/false, never the row). Name comparisons run in the DB against the column's
/// <c>utf8mb4_unicode_ci</c> collation, so they are case- AND accent-insensitive (OQ5). "Unique
/// active name per ledger" and reactivation-on-name-reuse are enforced atomically inside the write
/// transaction (no check-then-act race). Soft-delete uses the built-in <c>BaseRepository.Query</c>
/// filter.
/// </summary>
public interface ICategoryRepository : IBaseRepository, IQueryRepository<Category>
{
    /// <summary>The current user's categories, sorted default-first then name A-&gt;Z (OQ11). Excludes soft-deleted rows unless <paramref name="includeDeleted"/>.</summary>
    Task<IReadOnlyList<Category>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default);

    /// <summary>Resource-owned lookup by UUID (includes soft-deleted rows so callers decide). Null on an ownership miss.</summary>
    Task<Category?> GetByUuidAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default);

    /// <summary>An active category with this name (case/accent-insensitive), if any. Null otherwise.</summary>
    Task<Category?> FindActiveByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>A soft-deleted category with this name (case/accent-insensitive), if any. Null otherwise.</summary>
    Task<Category?> FindDeletedByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic create-path (OQ4): active name collision -&gt; <see cref="NameWriteStatus.NameDuplicate"/>;
    /// else a soft-deleted same-name row is reactivated (clears <c>IsDeleted</c>, overwrites color/icon
    /// per OQ5, default flag untouched); else a new (never-default) row is inserted. Unknown user -&gt;
    /// <see cref="NameWriteStatus.NotFound"/>.
    /// </summary>
    Task<NameWriteResult<Category>> CreateAsync(string userUuid, string name, string color, string? icon, CancellationToken cancellationToken = default);

    /// <summary>Updates name/color/icon (never the default flag) with an in-transaction uniqueness re-check excluding self. Miss -&gt; NotFound; active collision -&gt; NameDuplicate.</summary>
    Task<NameWriteResult<Category>> UpdateAsync(string userUuid, string categoryUuid, string name, string color, string? icon, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a category scoped to the user (idempotent). The default guard is enforced in the service. False on an ownership miss.</summary>
    Task<bool> SoftDeleteAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default);

    /// <summary>Atomic default swap: clears the current default and sets the target (active, owned) in one transaction. False when the target is missing or soft-deleted.</summary>
    Task<bool> SetDefaultAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default);

    /// <summary>True when the user has at least one active category.</summary>
    Task<bool> HasAnyCategoryAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>UUIDs of users lacking an active default category (for the backfill, OQ3).</summary>
    Task<IReadOnlyList<string>> GetUserUuidsWithoutDefaultCategoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent per-user backfill: within one transaction, seeds the suggested set when the user
    /// has no active category, or elects a default when the user has active categories but none is
    /// default. No-op (false) when the user already has an active default or is unknown.
    /// </summary>
    Task<bool> SeedSuggestedOrElectDefaultAsync(string userUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(ICategoryRepository))]
public sealed class CategoryRepository(AppDbContext dbContext) : BaseRepository(dbContext), ICategoryRepository
{
    public IQueryable<Category> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<Category>(tracking, includeDeleted);

    public Task<IReadOnlyList<Category>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var categories = await Query(includeDeleted: includeDeleted)
                .Where(category => category.User.Uuid == userUuid)
                .OrderByDescending(category => category.IsDefault)
                .ThenBy(category => category.Name)
                .ToListAsync(ct);
            return (IReadOnlyList<Category>)categories;
        }, cancellationToken);

    public Task<Category?> GetByUuidAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query(includeDeleted: true)
            .FirstOrDefaultAsync(category => category.Uuid == categoryUuid && category.User.Uuid == userUuid, ct), cancellationToken);

    public Task<Category?> FindActiveByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .FirstOrDefaultAsync(category => category.User.Uuid == userUuid && category.Name == name, ct), cancellationToken);

    public Task<Category?> FindDeletedByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query(includeDeleted: true)
            .FirstOrDefaultAsync(category => category.User.Uuid == userUuid && category.IsDeleted && category.Name == name, ct), cancellationToken);

    public Task<NameWriteResult<Category>> CreateAsync(string userUuid, string name, string color, string? icon, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return NameWriteResult<Category>.NotFound();
            }

            // Atomic check-then-act: an active same-name row blocks the create (OQ5 collation match).
            var activeExists = await db.Categories.AsNoTracking()
                .AnyAsync(category => category.UserId == userId && !category.IsDeleted && category.Name == name, cancellationToken);
            if (activeExists)
            {
                transaction.NoCommit();
                return NameWriteResult<Category>.NameDuplicate();
            }

            // A soft-deleted same-name row is revived instead of duplicating (OQ4): clear the flag,
            // overwrite color/icon with the request values (OQ5), leave the default flag untouched.
            var deleted = await db.Categories
                .FirstOrDefaultAsync(category => category.UserId == userId && category.IsDeleted && category.Name == name, cancellationToken);
            if (deleted is not null)
            {
                deleted.IsDeleted = false;
                deleted.Color = color;
                deleted.Icon = icon;
                return NameWriteResult<Category>.Reactivated(deleted);
            }

            // A category created through the API is never the default (that is set via SetDefaultAsync).
            var category = new Category
            {
                UserId = userId.Value,
                Name = name,
                Color = color,
                Icon = icon
            };
            db.Categories.Add(category);
            return NameWriteResult<Category>.Created(category);
        }, cancellationToken);

    public Task<NameWriteResult<Category>> UpdateAsync(string userUuid, string categoryUuid, string name, string color, string? icon, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var category = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == categoryUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (category is null)
            {
                transaction.NoCommit();
                return NameWriteResult<Category>.NotFound();
            }

            // Uniqueness re-check excluding self (active rows only), against the DB collation.
            var duplicate = await db.Categories.AsNoTracking()
                .AnyAsync(existing => existing.UserId == category.UserId
                    && !existing.IsDeleted
                    && existing.Id != category.Id
                    && existing.Name == name, cancellationToken);
            if (duplicate)
            {
                transaction.NoCommit();
                return NameWriteResult<Category>.NameDuplicate();
            }

            category.Name = name;
            category.Color = color;
            category.Icon = icon;
            return NameWriteResult<Category>.Updated(category);
        }, cancellationToken);

    public Task<bool> SoftDeleteAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            // includeDeleted: an already-deleted category is a harmless no-op success, not a miss.
            var category = await Query(tracking: true, includeDeleted: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == categoryUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (category is null)
            {
                transaction.NoCommit();
                return false;
            }

            category.IsDeleted = true;
            return true;
        }, cancellationToken);

    public Task<bool> SetDefaultAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            // Only an active, owned category can become the default (Query excludes soft-deleted).
            var target = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == categoryUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (target is null)
            {
                transaction.NoCommit();
                return false;
            }

            if (!target.IsDefault)
            {
                var current = await Query(tracking: true)
                    .FirstOrDefaultAsync(existing => existing.User.Uuid == userUuid && existing.IsDefault, cancellationToken);
                if (current is not null)
                    current.IsDefault = false;

                target.IsDefault = true;
            }

            return true;
        }, cancellationToken);

    public Task<bool> HasAnyCategoryAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .AnyAsync(category => category.User.Uuid == userUuid, ct), cancellationToken);

    public Task<IReadOnlyList<string>> GetUserUuidsWithoutDefaultCategoryAsync(CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (db, ct) =>
        {
            var uuids = await db.Users.AsNoTracking()
                .Where(user => !db.Categories
                    .Any(category => category.UserId == user.Id && category.IsDefault && !category.IsDeleted))
                .Select(user => user.Uuid)
                .ToListAsync(ct);
            return (IReadOnlyList<string>)uuids;
        }, cancellationToken);

    public Task<bool> SeedSuggestedOrElectDefaultAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return false;
            }

            // Idempotency re-check inside the transaction: bail if a default already exists.
            var hasDefault = await db.Categories.AsNoTracking()
                .AnyAsync(category => category.UserId == userId && category.IsDefault && !category.IsDeleted, cancellationToken);
            if (hasDefault)
            {
                transaction.NoCommit();
                return false;
            }

            // The user has active categories but no default (unexpected state): elect one instead of
            // seeding a second copy - prefer the suggested default name, else the first name A->Z.
            var actives = await db.Categories
                .Where(category => category.UserId == userId && !category.IsDeleted)
                .OrderBy(category => category.Name)
                .ToListAsync(cancellationToken);
            if (actives.Count > 0)
            {
                var defaultName = Category.SuggestedCategories.First(suggested => suggested.IsDefault).Name;
                var elected = actives.FirstOrDefault(category => category.Name == defaultName) ?? actives[0];
                elected.IsDefault = true;
                return true;
            }

            // No categories yet: seed the suggested set (one default) for this pre-existing user.
            db.Categories.AddRange(Category.BuildSuggestedSet(userId.Value));
            return true;
        }, cancellationToken);

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
