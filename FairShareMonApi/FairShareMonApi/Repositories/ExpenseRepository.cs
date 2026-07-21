using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Audit;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="Expense"/> rows and their shares/tags. Every read/write is
/// resource-owned: scoped by the owning user's UUID so another user's expenses are invisible (an
/// ownership miss yields null/ExpenseNotFound, never the row). Writes are single
/// <c>ExecuteTransactionAsync</c> blocks (§4.5): create/update/delete stage their audit rows via
/// <see cref="IAuditLogFactory"/> inside the same transaction (OQ20), so a rejected write
/// (<c>NoCommit</c>) leaves no expense, no shares, no expense_tags, and no audit (§3.8). Expenses are
/// hard-deleted (not <c>IEntityDeletable</c>); deleting cascades shares + expense_tags. The expense
/// total is derived (<c>SUM(shares.amount)</c>) - there is no stored total (OQ1).
/// </summary>
public interface IExpenseRepository : IBaseRepository, IQueryRepository<Expense>
{
    /// <summary>Resource-owned list with AND-combined filters (OQ13); sorted expense_time DESC. Includes category/payer/shares/tags for the summary projection.</summary>
    Task<IReadOnlyList<Expense>> ListByUserAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Resource-owned full load (shares + members, category, payer, tags). Null on an ownership miss.</summary>
    Task<Expense?> GetByUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);

    /// <summary>Atomic create with shares + tags + audit (§4.5); defaults + link-validation per the Step-5 flow.</summary>
    Task<ExpenseWriteResult<Expense>> CreateAsync(string userUuid, CreateExpenseData data, CancellationToken cancellationToken = default);

    /// <summary>Updates general info (name/description/expense_time/payer/category) + full tag-set replace (OQ18); stages an Update audit unless no-op.</summary>
    Task<ExpenseWriteResult<Expense>> UpdateGeneralInfoAsync(string userUuid, string expenseUuid, UpdateExpenseData data, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the expense (cascades shares + expense_tags); stages Delete audits before removal.</summary>
    Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);

    /// <summary>Sets the settled flag + settled_at and cascades to the billable shares (settled-per-member OQ3a); no audit (OQ11). The sole §4.4 exception: NOT guarded against a closed event (M6, OQ13).</summary>
    Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, bool isSettled, CancellationToken cancellationToken = default);

    /// <summary>Assigns/moves the expense to an event (owned + OPEN + within range); a CLOSED source or target -&gt; EventClosed (M6, OQ4/OQ16). No audit (OQ6).</summary>
    Task<ExpenseWriteResult<Expense>> AssignEventAsync(string userUuid, string expenseUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>Removes the expense from its event (-&gt; loose); idempotent no-op if already loose; a CLOSED current event -&gt; EventClosed; expense miss -&gt; ExpenseNotFound (M6, OQ4). No audit (OQ6).</summary>
    Task<ExpenseWriteStatus> RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);

    /// <summary>DB-side count of the user's expenses whose <c>expense_time</c> falls in the UTC half-open window <c>[from, to)</c> (M10 monthly limit; the service computes the +7 calendar-month window, OQ4a).</summary>
    Task<int> CountByUserInRangeAsync(string userUuid, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IExpenseRepository))]
public sealed class ExpenseRepository(AppDbContext dbContext, IAuditLogFactory auditLogFactory)
    : BaseRepository(dbContext), IExpenseRepository
{
    public IQueryable<Expense> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<Expense>(tracking, includeDeleted);

    public Task<IReadOnlyList<Expense>> ListByUserAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var query = Query().Where(expense => expense.User.Uuid == userUuid);

            if (filter.From.HasValue)
                query = query.Where(expense => expense.ExpenseTime >= filter.From.Value);
            if (filter.To.HasValue)
                query = query.Where(expense => expense.ExpenseTime <= filter.To.Value);
            if (!string.IsNullOrEmpty(filter.CategoryUuid))
                query = query.Where(expense => expense.Category.Uuid == filter.CategoryUuid);
            if (!string.IsNullOrEmpty(filter.TagUuid))
                query = query.Where(expense => expense.ExpenseTags.Any(link => link.Tag.Uuid == filter.TagUuid));
            if (filter.Settled.HasValue)
                query = query.Where(expense => expense.IsSettled == filter.Settled.Value);
            if (!string.IsNullOrEmpty(filter.EventUuid))
                query = query.Where(expense => expense.Event != null && expense.Event.Uuid == filter.EventUuid);
            if (filter.LooseOnly == true)
                query = query.Where(expense => expense.EventId == null);

            var expenses = await query
                .Include(expense => expense.Category)
                .Include(expense => expense.PayerMember)
                .Include(expense => expense.Shares)
                .Include(expense => expense.ExpenseTags).ThenInclude(link => link.Tag)
                .Include(expense => expense.Event)
                .OrderByDescending(expense => expense.ExpenseTime)
                .ThenByDescending(expense => expense.CreatedAt)
                .ToListAsync(ct);
            return (IReadOnlyList<Expense>)expenses;
        }, cancellationToken);

    public Task<int> CountByUserInRangeAsync(string userUuid, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .Where(expense => expense.User.Uuid == userUuid
                && expense.ExpenseTime >= fromUtcInclusive
                && expense.ExpenseTime < toUtcExclusive)
            .CountAsync(ct), cancellationToken);

    public Task<Expense?> GetByUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .Include(expense => expense.Category)
            .Include(expense => expense.PayerMember)
            .Include(expense => expense.Shares).ThenInclude(share => share.Member)
            .Include(expense => expense.ExpenseTags).ThenInclude(link => link.Tag)
            .Include(expense => expense.Event)
            .FirstOrDefaultAsync(expense => expense.Uuid == expenseUuid && expense.User.Uuid == userUuid, ct), cancellationToken);

    public Task<ExpenseWriteResult<Expense>> CreateAsync(string userUuid, CreateExpenseData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            // 1. Resolve owner.
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseNotFound);

            // 2 + 3. Resolve payer (default owner-rep) and validate it is owned + active.
            var payer = string.IsNullOrEmpty(data.PayerMemberUuid)
                ? await FindOwnerRepAsync(db, userId.Value, cancellationToken)
                : await FindActiveMemberAsync(db, userId.Value, data.PayerMemberUuid, cancellationToken);
            if (payer is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.PayerInvalid);

            // 2 + 3. Resolve category (default) and validate it is owned + active.
            var category = string.IsNullOrEmpty(data.CategoryUuid)
                ? await FindDefaultCategoryAsync(db, userId.Value, cancellationToken)
                : await FindActiveCategoryAsync(db, userId.Value, data.CategoryUuid, cancellationToken);
            if (category is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.CategoryInvalid);

            // 3. Validate every tag is owned + active.
            var tags = new List<Tag>();
            foreach (var tagUuid in data.TagUuids.Distinct())
            {
                var tag = await FindActiveTagAsync(db, userId.Value, tagUuid, cancellationToken);
                if (tag is null)
                    return Abort<Expense>(transaction, ExpenseWriteStatus.TagInvalid);
                tags.Add(tag);
            }

            // 3 + 5. Validate every share member is owned + active; reject duplicates.
            var resolvedShares = new List<(Member Member, decimal Amount, string? Note)>();
            var memberIds = new HashSet<ulong>();
            foreach (var shareData in data.Shares)
            {
                var member = await FindActiveMemberAsync(db, userId.Value, shareData.MemberUuid, cancellationToken);
                if (member is null)
                    return Abort<Expense>(transaction, ExpenseWriteStatus.ShareMemberInvalid);
                if (!memberIds.Add(member.Id))
                    return Abort<Expense>(transaction, ExpenseWriteStatus.DuplicateShareMember);
                resolvedShares.Add((member, shareData.Amount, shareData.Note));
            }

            // 4. Auto-inject a 0đ owner-rep share when absent (§5, OQ4).
            var ownerRep = payer.IsOwnerRepresentative ? payer : await FindOwnerRepAsync(db, userId.Value, cancellationToken);
            if (ownerRep is not null && memberIds.Add(ownerRep.Id))
                resolvedShares.Add((ownerRep, 0m, null));

            // 5b. Optional create-into-event (M6, OQ5): the target must be owned + OPEN and hold the
            // expense_time within its range; else the whole create aborts (9000/9001/9002).
            ulong? eventId = null;
            if (!string.IsNullOrEmpty(data.EventUuid))
            {
                var targetEvent = await FindEventAsync(db, userId.Value, data.EventUuid, cancellationToken);
                if (targetEvent is null)
                    return Abort<Expense>(transaction, ExpenseWriteStatus.EventNotFound);
                if (targetEvent.IsClosed)
                    return Abort<Expense>(transaction, ExpenseWriteStatus.EventClosed);
                if (!IsWithinRange(data.ExpenseTime, targetEvent))
                    return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseTimeOutOfEventRange);
                eventId = targetEvent.Id;
            }

            // 6. Insert expense + shares + expense_tags (FKs filled via the Expense navigation).
            var expense = new Expense
            {
                UserId = userId.Value,
                Name = data.Name,
                Description = data.Description,
                ExpenseTime = data.ExpenseTime,
                PayerMemberId = payer.Id,
                CategoryId = category.Id,
                EventId = eventId,
                IsSettled = false
            };
            db.Expenses.Add(expense);

            // 7. Stage the expense-create audit (1 row) + one share-create audit per share (OQ10).
            StageAudit(db, auditLogFactory.BuildExpenseAudit(
                AuditAction.Create, before: null, after: ExpenseAuditSnapshot.From(expense, payer, category, tags), userId.Value));

            foreach (var (member, amount, note) in resolvedShares)
            {
                var share = new Share { Expense = expense, MemberId = member.Id, Amount = amount, Note = note };
                db.Shares.Add(share);
                StageAudit(db, auditLogFactory.BuildShareAudit(
                    AuditAction.Create, before: null, after: ShareAuditSnapshot.From(share, expense.Uuid, member), userId.Value));
            }

            foreach (var tag in tags)
                db.ExpenseTags.Add(new ExpenseTag { Expense = expense, TagId = tag.Id });

            // 8. Commit - the whole expense + shares + expense_tags + audit is one atomic unit.
            return ExpenseWriteResult<Expense>.Success(expense);
        }, cancellationToken);

    public Task<ExpenseWriteResult<Expense>> UpdateGeneralInfoAsync(string userUuid, string expenseUuid, UpdateExpenseData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var expense = await Query(tracking: true)
                .Include(entity => entity.PayerMember)
                .Include(entity => entity.Category)
                .Include(entity => entity.ExpenseTags).ThenInclude(link => link.Tag)
                .Include(entity => entity.Event)
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseNotFound);

            // Closed-event write block (§4.4, OQ13): a CLOSED event rejects the whole general-info edit.
            if (EventWriteGuard.IsCurrentEventClosed(expense))
                return Abort<Expense>(transaction, ExpenseWriteStatus.EventClosed);

            // Within-range re-validation on the expense_time-edit path (OQ1/OQ7): for an (open) assigned
            // expense the new time must still fall in the event's range.
            if (expense.Event is not null && !IsWithinRange(data.ExpenseTime, expense.Event))
                return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseTimeOutOfEventRange);

            var userId = expense.UserId;

            // Snapshot BEFORE mutating (names come from the loaded links, which include soft-deleted rows).
            var before = ExpenseAuditSnapshot.From(
                expense, expense.PayerMember, expense.Category, expense.ExpenseTags.Select(link => link.Tag).ToList());

            var payer = string.IsNullOrEmpty(data.PayerMemberUuid)
                ? await FindOwnerRepAsync(db, userId, cancellationToken)
                : await FindActiveMemberAsync(db, userId, data.PayerMemberUuid, cancellationToken);
            if (payer is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.PayerInvalid);

            var category = string.IsNullOrEmpty(data.CategoryUuid)
                ? await FindDefaultCategoryAsync(db, userId, cancellationToken)
                : await FindActiveCategoryAsync(db, userId, data.CategoryUuid, cancellationToken);
            if (category is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.CategoryInvalid);

            var tags = new List<Tag>();
            foreach (var tagUuid in data.TagUuids.Distinct())
            {
                var tag = await FindActiveTagAsync(db, userId, tagUuid, cancellationToken);
                if (tag is null)
                    return Abort<Expense>(transaction, ExpenseWriteStatus.TagInvalid);
                tags.Add(tag);
            }

            expense.Name = data.Name;
            expense.Description = data.Description;
            expense.ExpenseTime = data.ExpenseTime;
            expense.PayerMemberId = payer.Id;
            expense.CategoryId = category.Id;

            // Full tag-set replace (OQ18): diff current vs desired, add/remove join rows.
            var desiredTagIds = tags.Select(tag => tag.Id).ToHashSet();
            var currentTagIds = expense.ExpenseTags.Select(link => link.TagId).ToHashSet();
            foreach (var link in expense.ExpenseTags.Where(link => !desiredTagIds.Contains(link.TagId)).ToList())
                db.ExpenseTags.Remove(link);
            foreach (var tag in tags.Where(tag => !currentTagIds.Contains(tag.Id)))
                db.ExpenseTags.Add(new ExpenseTag { ExpenseId = expense.Id, TagId = tag.Id });

            var after = ExpenseAuditSnapshot.From(expense, payer, category, tags);
            StageAudit(db, auditLogFactory.BuildExpenseAudit(AuditAction.Update, before, after, userId));

            return ExpenseWriteResult<Expense>.Success(expense);
        }, cancellationToken);

    public Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var expense = await Query(tracking: true)
                .Include(entity => entity.PayerMember)
                .Include(entity => entity.Category)
                .Include(entity => entity.Shares).ThenInclude(share => share.Member)
                .Include(entity => entity.ExpenseTags).ThenInclude(link => link.Tag)
                .Include(entity => entity.Event)
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ExpenseNotFound;
            }

            // Closed-event write block (§4.4, OQ13): a CLOSED event blocks deleting its expense.
            if (EventWriteGuard.IsCurrentEventClosed(expense))
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.EventClosed;
            }

            var userId = expense.UserId;
            var tags = expense.ExpenseTags.Select(link => link.Tag).ToList();

            // Stage the Delete audits (before-state) BEFORE the cascade removes the data (OQ10).
            StageAudit(db, auditLogFactory.BuildExpenseAudit(
                AuditAction.Delete, before: ExpenseAuditSnapshot.From(expense, expense.PayerMember, expense.Category, tags), after: null, userId));
            foreach (var share in expense.Shares)
                StageAudit(db, auditLogFactory.BuildShareAudit(
                    AuditAction.Delete, before: ShareAuditSnapshot.From(share, expense.Uuid, share.Member), after: null, userId));

            db.Expenses.Remove(expense);
            return ExpenseWriteStatus.Success;
        }, cancellationToken);

    public Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, bool isSettled, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var expense = await Query(tracking: true)
                .Include(entity => entity.Shares)
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ExpenseNotFound;
            }

            var now = AppDateTime.Now;
            expense.IsSettled = isSettled;
            expense.SettledAt = isSettled ? now : null;
            // Cascade the whole-expense toggle to its billable shares so the two layers stay consistent
            // (settled-per-member OQ3a); payer-own + 0đ shares are left untouched (OQ6a).
            SettlementReconciler.CascadeToShares(expense, isSettled, now);
            // No audit for a settled toggle (OQ11). No closed-event guard - the sole §4.4 exception (OQ13).
            return ExpenseWriteStatus.Success;
        }, cancellationToken);

    public Task<ExpenseWriteResult<Expense>> AssignEventAsync(string userUuid, string expenseUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var expense = await Query(tracking: true)
                .Include(entity => entity.Event)
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseNotFound);

            // Can't move an expense out of a CLOSED source event (§4.4, OQ16).
            if (EventWriteGuard.IsCurrentEventClosed(expense))
                return Abort<Expense>(transaction, ExpenseWriteStatus.EventClosed);

            // The target event must be owned + OPEN and hold the expense_time within its range.
            var targetEvent = await FindEventAsync(db, expense.UserId, eventUuid, cancellationToken);
            if (targetEvent is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.EventNotFound);
            if (targetEvent.IsClosed)
                return Abort<Expense>(transaction, ExpenseWriteStatus.EventClosed);
            if (!IsWithinRange(expense.ExpenseTime, targetEvent))
                return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseTimeOutOfEventRange);

            expense.EventId = targetEvent.Id;
            // No audit (OQ6).
            return ExpenseWriteResult<Expense>.Success(expense);
        }, cancellationToken);

    public Task<ExpenseWriteStatus> RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var expense = await Query(tracking: true)
                .Include(entity => entity.Event)
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ExpenseNotFound;
            }

            // Already loose: idempotent no-op success (OQ4).
            if (expense.EventId is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.Success;
            }

            // Remove only while OPEN - can't detach from a CLOSED event (§3.6/§4.4, OQ13).
            if (EventWriteGuard.IsCurrentEventClosed(expense))
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.EventClosed;
            }

            expense.EventId = null;
            // No audit (OQ6).
            return ExpenseWriteStatus.Success;
        }, cancellationToken);

    private static ExpenseWriteResult<T> Abort<T>(TransactionContext transaction, ExpenseWriteStatus status) where T : class
    {
        transaction.NoCommit();
        return ExpenseWriteResult<T>.Fail(status);
    }

    private static void StageAudit(AppDbContext db, AuditLog? log)
    {
        if (log is not null)
            db.AuditLogs.Add(log);
    }

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static Task<Member?> FindActiveMemberAsync(AppDbContext db, ulong userId, string memberUuid, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(member => member.UserId == userId && member.Uuid == memberUuid && !member.IsDeleted, cancellationToken);

    private static Task<Member?> FindOwnerRepAsync(AppDbContext db, ulong userId, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(member => member.UserId == userId && member.IsOwnerRepresentative && !member.IsDeleted, cancellationToken);

    private static Task<Category?> FindActiveCategoryAsync(AppDbContext db, ulong userId, string categoryUuid, CancellationToken cancellationToken) =>
        db.Categories.FirstOrDefaultAsync(category => category.UserId == userId && category.Uuid == categoryUuid && !category.IsDeleted, cancellationToken);

    private static Task<Category?> FindDefaultCategoryAsync(AppDbContext db, ulong userId, CancellationToken cancellationToken) =>
        db.Categories.FirstOrDefaultAsync(category => category.UserId == userId && category.IsDefault && !category.IsDeleted, cancellationToken);

    private static Task<Tag?> FindActiveTagAsync(AppDbContext db, ulong userId, string tagUuid, CancellationToken cancellationToken) =>
        db.Tags.FirstOrDefaultAsync(tag => tag.UserId == userId && tag.Uuid == tagUuid && !tag.IsDeleted, cancellationToken);

    private static Task<Event?> FindEventAsync(AppDbContext db, ulong userId, string eventUuid, CancellationToken cancellationToken) =>
        db.Events.AsNoTracking().FirstOrDefaultAsync(evt => evt.UserId == userId && evt.Uuid == eventUuid, cancellationToken);

    /// <summary>Whole-day-inclusive range check against the normalized event window (OQ1).</summary>
    private static bool IsWithinRange(DateTime expenseTime, Event evt) =>
        expenseTime >= evt.StartDate && expenseTime <= evt.EndDate;
}
