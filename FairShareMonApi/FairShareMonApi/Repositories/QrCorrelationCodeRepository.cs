using System.Security.Cryptography;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>Which settlement flag a resolved <see cref="CorrelationTarget"/> maps to.</summary>
public enum CorrelationTargetKind
{
    /// <summary>An individual share (<see cref="ISharesService.SetSettledAsync"/>) - the code's <c>ExpenseId</c> is set.</summary>
    Share,

    /// <summary>A member's per-event net clearance flag (<see cref="IEventsService.SetMemberSettledAsync"/>) - the code's <c>ExpenseId</c> is null.</summary>
    EventMember
}

/// <summary>
/// The live-resolved target a correlation code currently points to (planning/bank-callback-settlement.md
/// Step 2), re-derived at APPLY time - never trusts <see cref="QrCorrelationCode.ExpectedAmountSnapshot"/>
/// (mirrors event-expense-settlement-sync's "recompute live" precedent, OQ1 there).
/// </summary>
public sealed record CorrelationTarget(
    ulong CorrelationCodeId,
    ulong UserId,
    CorrelationTargetKind Kind,
    string UserUuid,
    string? EventUuid,
    string MemberUuid,
    string? ExpenseUuid,
    string? ShareUuid,
    decimal CurrentExpectedAmount,
    bool IsAlreadySettled);

/// <summary>
/// Data access for <see cref="QrCorrelationCode"/> rows: owner-scoped find-or-create at QR-generation
/// time, and an ANONYMOUS lookup-by-code the bank-callback webhook path uses (mirrors
/// <see cref="EventShareLinkRepository"/>'s owner-scoped-write / anonymous-read split).
/// </summary>
public interface IQrCorrelationCodeRepository : IBaseRepository
{
    /// <summary>
    /// Resolves the owner-scoped <c>User</c>/<c>Event?</c>/<c>Member</c>/<c>Expense?</c> ids (defensive -
    /// the caller, <c>WalletQrService</c>, already resolved these via its own resource-owned services), then
    /// (OQ2) reuses the most recent still-valid code for the exact same
    /// <c>(User, Event?, Member, Expense?, ExpectedAmount)</c> tuple if one exists, else generates a fresh
    /// one (OQ1's alphabet/length, retried on a unique-index collision) with a 90-day TTL.
    /// </summary>
    Task<QrCorrelationCode> GetOrCreateAsync(
        string userUuid,
        string? eventUuid,
        string memberUuid,
        string? expenseUuid,
        decimal expectedAmount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymous lookup (NOT user-scoped) the bank-callback webhook path uses: resolves the code to its
    /// LIVE current target - a null return means "unmatched" (unknown code, expired, or a defensive
    /// share-resolution miss), never an exception.
    /// </summary>
    Task<CorrelationTarget?> ResolveCurrentTargetAsync(string code, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IQrCorrelationCodeRepository))]
public sealed class QrCorrelationCodeRepository(AppDbContext dbContext) : BaseRepository(dbContext), IQrCorrelationCodeRepository
{
    private const int MaxGenerationAttempts = 20;
    private const int ExpiresAfterDays = 90;

    public Task<QrCorrelationCode> GetOrCreateAsync(
        string userUuid,
        string? eventUuid,
        string memberUuid,
        string? expenseUuid,
        decimal expectedAmount,
        CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var user = await Query<User>().FirstOrDefaultAsync(u => u.Uuid == userUuid, cancellationToken);
            if (user is null)
            {
                transaction.NoCommit();
                throw new ErrorException(ErrorCodes.Unauthorized, MessageKeys.Error.Unauthorized);
            }

            ulong? eventId = null;
            if (!string.IsNullOrWhiteSpace(eventUuid))
            {
                var evt = await Query<Event>()
                    .FirstOrDefaultAsync(e => e.Uuid == eventUuid && e.UserId == user.Id, cancellationToken);
                if (evt is null)
                {
                    transaction.NoCommit();
                    throw new ErrorException(ErrorCodes.EventNotFound, MessageKeys.Error.EventNotFound);
                }

                eventId = evt.Id;
            }

            var member = await Query<Member>()
                .FirstOrDefaultAsync(m => m.Uuid == memberUuid && m.UserId == user.Id, cancellationToken);
            if (member is null)
            {
                transaction.NoCommit();
                throw new ErrorException(ErrorCodes.MemberNotFound, MessageKeys.Error.MemberNotFound);
            }

            ulong? expenseId = null;
            if (!string.IsNullOrWhiteSpace(expenseUuid))
            {
                var expense = await Query<Expense>()
                    .FirstOrDefaultAsync(e => e.Uuid == expenseUuid && e.UserId == user.Id, cancellationToken);
                if (expense is null)
                {
                    transaction.NoCommit();
                    throw new ErrorException(ErrorCodes.ExpenseNotFound, MessageKeys.Error.ExpenseNotFound);
                }

                expenseId = expense.Id;
            }

            var now = AppDateTime.Now;

            // OQ2: find-or-reuse for the exact same tuple, so a never-cached QR view/regen does not grow
            // this table unboundedly.
            var existing = await Query<QrCorrelationCode>()
                .Where(code => code.UserId == user.Id
                    && code.EventId == eventId
                    && code.MemberId == member.Id
                    && code.ExpenseId == expenseId
                    && code.ExpectedAmountSnapshot == expectedAmount
                    && (code.ExpiresAt == null || code.ExpiresAt > now))
                .OrderByDescending(code => code.CreatedAt)
                .ThenByDescending(code => code.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                transaction.NoCommit();
                return existing;
            }

            var freshCode = await GenerateUniqueCodeAsync(db, cancellationToken);
            var row = new QrCorrelationCode
            {
                UserId = user.Id,
                EventId = eventId,
                MemberId = member.Id,
                ExpenseId = expenseId,
                Code = freshCode,
                ExpectedAmountSnapshot = expectedAmount,
                ExpiresAt = now.AddDays(ExpiresAfterDays)
            };
            db.QrCorrelationCodes.Add(row);
            return row;
        }, cancellationToken);

    public Task<CorrelationTarget?> ResolveCurrentTargetAsync(string code, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (db, ct) =>
        {
            var now = AppDateTime.Now;
            var row = await Query<QrCorrelationCode>()
                .Include(c => c.User)
                .Include(c => c.Member)
                .Include(c => c.Event)
                .Include(c => c.Expense).ThenInclude(e => e!.Shares)
                .FirstOrDefaultAsync(c => c.Code == code, ct);
            if (row is null || (row.ExpiresAt is { } expiresAt && expiresAt <= now))
                return null;

            if (row.ExpenseId is not null)
            {
                // The unique (expense_id, member_id) share index (event-expense-settlement-sync Step M1.2)
                // guarantees at most one match; a null here is defensive only.
                var share = row.Expense?.Shares.FirstOrDefault(s => s.MemberId == row.MemberId);
                if (share is null)
                    return null;

                return new CorrelationTarget(
                    row.Id,
                    row.UserId,
                    CorrelationTargetKind.Share,
                    row.User.Uuid,
                    row.Event?.Uuid,
                    row.Member.Uuid,
                    row.Expense!.Uuid,
                    share.Uuid,
                    share.Amount,
                    share.IsSettled);
            }

            // EventMember target: NetOwed via the same canonical classifier Direction 1/2 already gate on.
            var eventId = row.EventId!.Value;
            var facts = await EventSettlementClassifier.ClassifyAsync(db, eventId, [row.MemberId], ct);
            var netOwed = facts.TryGetValue(row.MemberId, out var memberFacts) ? memberFacts.NetOwed : 0m;

            var clearedAmount = await Query<EventMemberSettlement>()
                .Where(s => s.EventId == eventId && s.MemberId == row.MemberId)
                .Select(s => (decimal?)s.ClearedAmount)
                .FirstOrDefaultAsync(ct) ?? 0m;
            var isAlreadySettled = netOwed <= 0m || clearedAmount >= netOwed;

            return new CorrelationTarget(
                row.Id,
                row.UserId,
                CorrelationTargetKind.EventMember,
                row.User.Uuid,
                row.Event!.Uuid,
                row.Member.Uuid,
                null,
                null,
                netOwed,
                isAlreadySettled);
        }, cancellationToken);

    /// <summary>Generates a fresh code (OQ1's prefix/alphabet/length), retrying on a unique-index collision (negligible at this feature's scale).</summary>
    private static async Task<string> GenerateUniqueCodeAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var candidate = QrCorrelationCode.CodePrefix + RandomSuffix(QrCorrelationCode.CodeRandomLength);
            var exists = await db.QrCorrelationCodes.AsNoTracking().AnyAsync(c => c.Code == candidate, cancellationToken);
            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException("Không thể tạo mã liên kết QR duy nhất sau nhiều lần thử.");
    }

    private static string RandomSuffix(int length)
    {
        Span<char> buffer = stackalloc char[length];
        for (var i = 0; i < length; i++)
            buffer[i] = QrCorrelationCode.CodeAlphabet[RandomNumberGenerator.GetInt32(QrCorrelationCode.CodeAlphabet.Length)];

        return new string(buffer);
    }
}
