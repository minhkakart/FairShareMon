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

    /// <summary>Sets the settled flag + settled_at; no audit (OQ11). A dedicated seam for M6's closed-event exception.</summary>
    Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, bool isSettled, CancellationToken cancellationToken = default);
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

            var expenses = await query
                .Include(expense => expense.Category)
                .Include(expense => expense.PayerMember)
                .Include(expense => expense.Shares)
                .Include(expense => expense.ExpenseTags).ThenInclude(link => link.Tag)
                .OrderByDescending(expense => expense.ExpenseTime)
                .ThenByDescending(expense => expense.CreatedAt)
                .ToListAsync(ct);
            return (IReadOnlyList<Expense>)expenses;
        }, cancellationToken);

    public Task<Expense?> GetByUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .Include(expense => expense.Category)
            .Include(expense => expense.PayerMember)
            .Include(expense => expense.Shares).ThenInclude(share => share.Member)
            .Include(expense => expense.ExpenseTags).ThenInclude(link => link.Tag)
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

            // 6. Insert expense + shares + expense_tags (FKs filled via the Expense navigation).
            var expense = new Expense
            {
                UserId = userId.Value,
                Name = data.Name,
                Description = data.Description,
                ExpenseTime = data.ExpenseTime,
                PayerMemberId = payer.Id,
                CategoryId = category.Id,
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
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
                return Abort<Expense>(transaction, ExpenseWriteStatus.ExpenseNotFound);

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
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ExpenseNotFound;
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
                .FirstOrDefaultAsync(entity => entity.Uuid == expenseUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (expense is null)
            {
                transaction.NoCommit();
                return ExpenseWriteStatus.ExpenseNotFound;
            }

            expense.IsSettled = isSettled;
            expense.SettledAt = isSettled ? AppDateTime.Now : null;
            // No audit for a settled toggle (OQ11).
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
}
