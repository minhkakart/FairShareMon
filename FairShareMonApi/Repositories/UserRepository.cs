using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace FairShareMonApi.Repositories;

/// <summary>Data access for <see cref="User"/> rows. Usernames are stored lowercase; lookups are
/// case-insensitive at the DB level (utf8mb4_unicode_ci).</summary>
public interface IUserRepository : IBaseRepository, IQueryRepository<User>
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the user in one transaction with an in-transaction uniqueness re-check plus
    /// unique-index race safety. Null when the username is already taken.
    /// </summary>
    Task<User?> CreateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the user and then runs <paramref name="bootstrap"/> INSIDE the same transaction,
    /// after an intermediate flush has assigned <see cref="User.Id"/> (so bootstrap steps can set
    /// FKs to it). The whole thing is atomic - a failure in the bootstrap rolls the user back too;
    /// the in-transaction uniqueness re-check and the unique-index race absorption are preserved
    /// exactly as in <see cref="CreateAsync"/>. Null when the username is already taken.
    /// </summary>
    Task<User?> CreateWithBootstrapAsync(
        User user,
        Func<AppDbContext, User, CancellationToken, Task> bootstrap,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the user's password hash. False when the user no longer exists.</summary>
    Task<bool> UpdatePasswordAsync(string uuid, string passwordHash, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IUserRepository))]
public sealed class UserRepository(AppDbContext dbContext) : BaseRepository(dbContext), IUserRepository
{
    public IQueryable<User> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<User>(tracking, includeDeleted);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query().FirstOrDefaultAsync(user => user.Username == username, ct), cancellationToken);

    public Task<User?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query().FirstOrDefaultAsync(user => user.Uuid == uuid, ct), cancellationToken);

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query().AnyAsync(user => user.Username == username, ct), cancellationToken);

    public Task<User?> CreateAsync(User user, CancellationToken cancellationToken = default) =>
        // No bootstrap: kept for callers/tests that only need the users row.
        CreateWithBootstrapAsync(user, static (_, _, _) => Task.CompletedTask, cancellationToken);

    public async Task<User?> CreateWithBootstrapAsync(
        User user,
        Func<AppDbContext, User, CancellationToken, Task> bootstrap,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteTransactionAsync(async (db, transaction) =>
            {
                var taken = await db.Users.AsNoTracking()
                    .AnyAsync(existing => existing.Username == user.Username, cancellationToken);
                if (taken)
                {
                    transaction.NoCommit();
                    return null;
                }

                db.Users.Add(user);
                // Intermediate flush (allowed exception to the "no trailing SaveChanges" rule):
                // assigns user.Id so bootstrap steps can set FKs to it. Still one transaction -
                // the extension's final SaveChanges + commit persists the bootstrapped rows.
                await db.SaveChangesAsync(cancellationToken);

                await bootstrap(db, user, cancellationToken);
                return user;
            }, cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // Unique-index race: another request registered the same username between the check
            // and the commit - treated exactly like the pre-checked duplicate.
            return null;
        }
    }

    public Task<bool> UpdatePasswordAsync(string uuid, string passwordHash, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(existing => existing.Uuid == uuid, cancellationToken);
            if (user is null)
            {
                transaction.NoCommit();
                return false;
            }

            user.PasswordHash = passwordHash;
            return true;
        }, cancellationToken);
}
