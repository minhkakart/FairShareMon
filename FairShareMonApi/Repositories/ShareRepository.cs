using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Audit;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for individual <see cref="Share"/> sub-routes on an expense. Every write is
/// resource-owned via the owning expense (scoped by the user's UUID) and runs in a single
/// <c>ExecuteTransactionAsync</c> that also stages the audit row (OQ20), so a rejected write leaves no
/// share and no audit (§3.8). Enforces link integrity (member owned + active, §4.2/§4.8),
/// one-share-per-member (OQ5), and the owner-representative-share protection (§5, OQ4): its share
/// cannot be deleted, and its member cannot be changed away.
/// </summary>
public interface IShareRepository : IBaseRepository
{
    /// <summary>Adds a share to the expense; link-validates the member and rejects a duplicate member (7001/7003).</summary>
    Task<ExpenseWriteResult<Share>> AddAsync(string userUuid, string expenseUuid, ShareData data, CancellationToken cancellationToken = default);

    /// <summary>Updates a share (amount/note/change-member); owner-rep member-change guard; stages an Update audit unless no-op.</summary>
    Task<ExpenseWriteResult<Share>> UpdateAsync(string userUuid, string expenseUuid, string shareUuid, ShareData data, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes a share; blocks deleting the owner-rep's share (7002); stages a Delete audit.</summary>
    Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, string shareUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IShareRepository))]
public sealed class ShareRepository(AppDbContext dbContext, IAuditLogFactory auditLogFactory)
    : BaseRepository(dbContext), IShareRepository
{
    public Task<ExpenseWriteResult<Share>> AddAsync(string userUuid, string expenseUuid, ShareData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var expense = await FindOwnedExpenseAsync(userUuid, expenseUuid, cancellationToken);
            if (expense is null)
                return Abort(transaction, ExpenseWriteStatus.ExpenseNotFound);

            // Closed-event write block (§4.4, OQ13): a CLOSED event rejects adding a share.
            if (EventWriteGuard.IsCurrentEventClosed(expense))
                return Abort(transaction, ExpenseWriteStatus.EventClosed);

            var member = await FindActiveMemberAsync(db, expense.UserId, data.MemberUuid, cancellationToken);
            if (member is null)
                return Abort(transaction, ExpenseWriteStatus.ShareMemberInvalid);

            var duplicate = await db.Shares.AsNoTracking()
                .AnyAsync(share => share.ExpenseId == expense.Id && share.MemberId == member.Id, cancellationToken);
            if (duplicate)
                return Abort(transaction, ExpenseWriteStatus.DuplicateShareMember);

            var newShare = new Share { ExpenseId = expense.Id, MemberId = member.Id, Amount = data.Amount, Note = data.Note, Member = member };
            db.Shares.Add(newShare);
            StageAudit(db, auditLogFactory.BuildShareAudit(
                AuditAction.Create, before: null, after: ShareAuditSnapshot.From(newShare, expense.Uuid, member), expense.UserId));

            return ExpenseWriteResult<Share>.Success(newShare);
        }, cancellationToken);

    public Task<ExpenseWriteResult<Share>> UpdateAsync(string userUuid, string expenseUuid, string shareUuid, ShareData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var expense = await FindOwnedExpenseAsync(userUuid, expenseUuid, cancellationToken);
            if (expense is null)
                return Abort(transaction, ExpenseWriteStatus.ShareNotFound);

            // Closed-event write block (§4.4, OQ13): a CLOSED event rejects editing a share.
            if (EventWriteGuard.IsCurrentEventClosed(expense))
                return Abort(transaction, ExpenseWriteStatus.EventClosed);

            var share = await Query<Share>(tracking: true)
                .Include(entity => entity.Member)
                .FirstOrDefaultAsync(entity => entity.Uuid == shareUuid && entity.ExpenseId == expense.Id, cancellationToken);
            if (share is null)
                return Abort(transaction, ExpenseWriteStatus.ShareNotFound);

            var currentMember = share.Member;
            var before = ShareAuditSnapshot.From(share, expense.Uuid, currentMember);

            Member targetMember;
            if (data.MemberUuid == currentMember.Uuid)
            {
                targetMember = currentMember;
            }
            else
            {
                // The owner-rep share's member cannot be changed away (§5, OQ4) - same 7002 protection.
                if (currentMember.IsOwnerRepresentative)
                    return Abort(transaction, ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable);

                var newMember = await FindActiveMemberAsync(db, expense.UserId, data.MemberUuid, cancellationToken);
                if (newMember is null)
                    return Abort(transaction, ExpenseWriteStatus.ShareMemberInvalid);

                var duplicate = await db.Shares.AsNoTracking()
                    .AnyAsync(other => other.ExpenseId == expense.Id && other.MemberId == newMember.Id && other.Id != share.Id, cancellationToken);
                if (duplicate)
                    return Abort(transaction, ExpenseWriteStatus.DuplicateShareMember);

                targetMember = newMember;
            }

            share.MemberId = targetMember.Id;
            share.Member = targetMember;
            share.Amount = data.Amount;
            share.Note = data.Note;

            var after = ShareAuditSnapshot.From(share, expense.Uuid, targetMember);
            StageAudit(db, auditLogFactory.BuildShareAudit(AuditAction.Update, before, after, expense.UserId));

            return ExpenseWriteResult<Share>.Success(share);
        }, cancellationToken);

    public Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, string shareUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var expense = await FindOwnedExpenseAsync(userUuid, expenseUuid, cancellationToken);
            if (expense is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ShareNotFound;
            }

            // Closed-event write block (§4.4, OQ13): a CLOSED event rejects deleting a share.
            if (EventWriteGuard.IsCurrentEventClosed(expense))
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.EventClosed;
            }

            var share = await Query<Share>(tracking: true)
                .Include(entity => entity.Member)
                .FirstOrDefaultAsync(entity => entity.Uuid == shareUuid && entity.ExpenseId == expense.Id, cancellationToken);
            if (share is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ShareNotFound;
            }

            // The owner-rep's share must always exist (§5, OQ4).
            if (share.Member.IsOwnerRepresentative)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable;
            }

            StageAudit(db, auditLogFactory.BuildShareAudit(
                AuditAction.Delete, before: ShareAuditSnapshot.From(share, expense.Uuid, share.Member), after: null, expense.UserId));
            db.Shares.Remove(share);

            return ExpenseWriteStatus.Success;
        }, cancellationToken);

    private Task<Expense?> FindOwnedExpenseAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken) =>
        Query<Expense>()
            .Include(expense => expense.Event)
            .FirstOrDefaultAsync(expense => expense.Uuid == expenseUuid && expense.User.Uuid == userUuid, cancellationToken);

    private static Task<Member?> FindActiveMemberAsync(AppDbContext db, ulong userId, string memberUuid, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(member => member.UserId == userId && member.Uuid == memberUuid && !member.IsDeleted, cancellationToken);

    private static ExpenseWriteResult<Share> Abort(TransactionContext transaction, ExpenseWriteStatus status)
    {
        transaction.NoCommit();
        return ExpenseWriteResult<Share>.Fail(status);
    }

    private static void StageAudit(AppDbContext db, AuditLog? log)
    {
        if (log is not null)
            db.AuditLogs.Add(log);
    }
}
