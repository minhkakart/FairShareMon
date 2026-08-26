using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>All fields needed to insert one <see cref="BankTransactionCallback"/> row (planning/bank-callback-settlement.md Step 3).</summary>
public sealed record BankTransactionCallbackData(
    string ProviderKey,
    string ProviderTransactionId,
    bool IsIncoming,
    decimal Amount,
    string? BankBin,
    string? DestinationAccountNumber,
    string Content,
    string? ExtractedCode,
    DateTime TransactionAt,
    string RawPayload,
    ulong? MatchedCorrelationCodeId,
    ulong? ResolvedUserId,
    BankCallbackOutcome Outcome,
    string? FailureNote,
    DateTime? AppliedAt);

/// <summary>
/// Data access for <see cref="BankTransactionCallback"/> rows: the idempotency dedup pre-check, the
/// insert (with a DB-level unique-index backstop against a concurrent-insert race), and the owner-facing
/// paginated list (OQ5's review endpoint, <c>GET api/v1/bank-callbacks</c>).
/// </summary>
public interface IBankTransactionCallbackRepository : IBaseRepository
{
    /// <summary>The idempotency pre-check (fast path, indexed) - a retried/duplicated webhook must not reprocess.</summary>
    Task<BankTransactionCallback?> FindByProviderTransactionAsync(string providerKey, string providerTransactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the row. A duplicate-key exception on insert (a race between the pre-check and this insert)
    /// is caught and treated as "already recorded" - returns the existing row, never surfaced as a 500.
    /// </summary>
    Task<BankTransactionCallback> RecordAsync(BankTransactionCallbackData data, CancellationToken cancellationToken = default);

    /// <summary>Owner-scoped, newest-first pagination (OQ5/OQ9, ungated) - another user's rows never appear.</summary>
    Task<(IReadOnlyList<BankTransactionCallback> Items, int Total)> ListByUserAsync(string userUuid, int limit, int offset, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IBankTransactionCallbackRepository))]
public sealed class BankTransactionCallbackRepository(AppDbContext dbContext) : BaseRepository(dbContext), IBankTransactionCallbackRepository
{
    public Task<BankTransactionCallback?> FindByProviderTransactionAsync(string providerKey, string providerTransactionId, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query<BankTransactionCallback>()
            .FirstOrDefaultAsync(callback => callback.ProviderKey == providerKey && callback.ProviderTransactionId == providerTransactionId, ct), cancellationToken);

    public Task<BankTransactionCallback> RecordAsync(BankTransactionCallbackData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var row = new BankTransactionCallback
            {
                ProviderKey = data.ProviderKey,
                ProviderTransactionId = data.ProviderTransactionId,
                IsIncoming = data.IsIncoming,
                Amount = data.Amount,
                BankBin = data.BankBin,
                DestinationAccountNumber = data.DestinationAccountNumber,
                Content = data.Content,
                ExtractedCode = data.ExtractedCode,
                TransactionAt = data.TransactionAt,
                RawPayload = data.RawPayload,
                MatchedCorrelationCodeId = data.MatchedCorrelationCodeId,
                ResolvedUserId = data.ResolvedUserId,
                Outcome = data.Outcome,
                FailureNote = data.FailureNote,
                AppliedAt = data.AppliedAt
            };
            db.BankTransactionCallbacks.Add(row);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent insert raced us to the unique (provider_key, provider_transaction_id) index -
                // the DB-level backstop behind the idempotency pre-check (Requirements). Never a 500.
                db.Entry(row).State = EntityState.Detached;
                var duplicate = await FindDuplicateAsync(db, data, cancellationToken);
                if (duplicate is null)
                    throw;

                transaction.NoCommit();
                return duplicate;
            }

            return row;
        }, cancellationToken);

    public Task<(IReadOnlyList<BankTransactionCallback> Items, int Total)> ListByUserAsync(string userUuid, int limit, int offset, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var query = Query<BankTransactionCallback>().Where(callback => callback.ResolvedUser != null && callback.ResolvedUser.Uuid == userUuid);
            var total = await query.CountAsync(ct);
            var items = await query
                .Include(callback => callback.MatchedCorrelationCode).ThenInclude(code => code!.Member)
                .Include(callback => callback.MatchedCorrelationCode).ThenInclude(code => code!.Event)
                .Include(callback => callback.MatchedCorrelationCode).ThenInclude(code => code!.Expense)
                .OrderByDescending(callback => callback.CreatedAt)
                .ThenByDescending(callback => callback.Id)
                .Skip(offset)
                .Take(limit)
                .ToListAsync(ct);

            return ((IReadOnlyList<BankTransactionCallback>)items, total);
        }, cancellationToken);

    private static Task<BankTransactionCallback?> FindDuplicateAsync(AppDbContext db, BankTransactionCallbackData data, CancellationToken cancellationToken) =>
        db.BankTransactionCallbacks.AsNoTracking()
            .FirstOrDefaultAsync(callback => callback.ProviderKey == data.ProviderKey && callback.ProviderTransactionId == data.ProviderTransactionId, cancellationToken);
}
