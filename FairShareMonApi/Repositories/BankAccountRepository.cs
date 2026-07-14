using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="BankAccount"/> rows (ví, The-ideal.md §3.10). Every read/write is
/// resource-owned: scoped by the owning user's UUID so another user's accounts are invisible (an
/// ownership miss yields null/false, never the row). Enforces the single-default invariant atomically
/// inside write transactions (mirroring the default-category swap): the first account added becomes
/// the default; <see cref="SetDefaultAsync"/> swaps it; <see cref="DeleteAsync"/> promotes another
/// account when the deleted one was the default. Accounts are <b>hard-deleted</b> (no soft-delete
/// filter - the entity is not <c>IEntityDeletable</c>, OQ7).
/// </summary>
public interface IBankAccountRepository : IBaseRepository, IQueryRepository<BankAccount>
{
    /// <summary>The current user's accounts, sorted default-first then most-recently-created (OQ6). Empty when the wallet has no accounts.</summary>
    Task<IReadOnlyList<BankAccount>> ListByUserAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>Resource-owned lookup by UUID. Null on an ownership miss.</summary>
    Task<BankAccount?> GetByUuidAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default);

    /// <summary>The user's default account, or null when the wallet is empty (OQ8/OQ11).</summary>
    Task<BankAccount?> GetDefaultAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new account; the user's <b>first</b> account is auto-set default (OQ6). Unknown user -&gt; null.</summary>
    Task<BankAccount?> CreateAsync(string userUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default);

    /// <summary>Tracked update scoped to the user; <b>never touches</b> <c>is_default</c> (OQ6). False on an ownership miss.</summary>
    Task<bool> UpdateAsync(string userUuid, string bankAccountUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the account (OQ7); if it was the default and others remain, promotes the most-recently-created remaining account to default (OQ6). False on an ownership miss.</summary>
    Task<bool> DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default);

    /// <summary>Atomic default swap: clears the current default and sets the target (owned) in one transaction. False when the target is missing.</summary>
    Task<bool> SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IBankAccountRepository))]
public sealed class BankAccountRepository(AppDbContext dbContext) : BaseRepository(dbContext), IBankAccountRepository
{
    public IQueryable<BankAccount> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<BankAccount>(tracking, includeDeleted);

    public Task<IReadOnlyList<BankAccount>> ListByUserAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var accounts = await Query()
                .Where(account => account.User.Uuid == userUuid)
                .OrderByDescending(account => account.IsDefault)
                .ThenByDescending(account => account.CreatedAt)
                .ThenByDescending(account => account.Id)
                .ToListAsync(ct);
            return (IReadOnlyList<BankAccount>)accounts;
        }, cancellationToken);

    public Task<BankAccount?> GetByUuidAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .FirstOrDefaultAsync(account => account.Uuid == bankAccountUuid && account.User.Uuid == userUuid, ct), cancellationToken);

    public Task<BankAccount?> GetDefaultAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .FirstOrDefaultAsync(account => account.User.Uuid == userUuid && account.IsDefault, ct), cancellationToken);

    public Task<BankAccount?> CreateAsync(string userUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return (BankAccount?)null;
            }

            // The first account in the wallet auto-becomes the default (OQ6).
            var hasAny = await db.BankAccounts.AsNoTracking()
                .AnyAsync(account => account.UserId == userId, cancellationToken);

            var account = new BankAccount
            {
                UserId = userId.Value,
                BankBin = bankBin,
                BankName = bankName,
                AccountNumber = accountNumber,
                AccountHolderName = accountHolderName,
                IsDefault = !hasAny
            };
            db.BankAccounts.Add(account);
            return account;
        }, cancellationToken);

    public Task<bool> UpdateAsync(string userUuid, string bankAccountUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var account = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == bankAccountUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (account is null)
            {
                transaction.NoCommit();
                return false;
            }

            // The update never touches is_default (that is set via SetDefaultAsync only, OQ6).
            account.BankBin = bankBin;
            account.BankName = bankName;
            account.AccountNumber = accountNumber;
            account.AccountHolderName = accountHolderName;
            return true;
        }, cancellationToken);

    public Task<bool> DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var account = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == bankAccountUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (account is null)
            {
                transaction.NoCommit();
                return false;
            }

            var wasDefault = account.IsDefault;
            db.BankAccounts.Remove(account);

            // Deleting the default promotes the most-recently-created remaining account (OQ6);
            // deleting the last account leaves the wallet empty (a valid state).
            if (wasDefault)
            {
                var promoted = await Query(tracking: true)
                    .Where(existing => existing.User.Uuid == userUuid && existing.Id != account.Id)
                    .OrderByDescending(existing => existing.CreatedAt)
                    .ThenByDescending(existing => existing.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (promoted is not null)
                    promoted.IsDefault = true;
            }

            return true;
        }, cancellationToken);

    public Task<bool> SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var target = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == bankAccountUuid && existing.User.Uuid == userUuid, cancellationToken);
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

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
