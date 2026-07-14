using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Audit;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Shared base for the M5 expense/share/audit integration tests against the real MariaDB (skippable).
/// Extends <see cref="AuthDbTestBase"/> with seed helpers for a ledger (user + owner-rep member +
/// default category) plus members/categories/tags, and repository factories wired with the real
/// (pure) <see cref="AuditLogFactory"/>.
///
/// Cleanup: <see cref="AuthDbTestBase.DisposeAsync"/> deletes the prefix's users, which cascades to
/// members/categories/tags/expenses (and shares/expense_tags via the expense cascade). The
/// <c>audit_logs</c> table's <c>entity_uuid</c>/<c>expense_uuid</c> carry NO FK, but its
/// <c>actor_user_id</c> FK cascades on user delete - so the base cascade already clears audit rows.
/// This base ALSO sweeps <c>audit_logs</c> explicitly by the prefix's actor first (defensive +
/// mirrors the orchestration sweep contract) so a leftover row can never survive a run.
/// </summary>
public abstract class ExpenseDbTestBase(DatabaseFixture fixture) : AuthDbTestBase(fixture)
{
    protected const string DefaultColor = "#F97316";

    protected ExpenseRepository CreateExpenseRepository() => new(CreateContext(), new AuditLogFactory());

    protected ShareRepository CreateShareRepository() => new(CreateContext(), new AuditLogFactory());

    protected AuditLogRepository CreateAuditLogRepository() => new(CreateContext());

    protected EventRepository CreateEventRepository() => new(CreateContext());

    /// <summary>
    /// Seeds an event row directly (no repository). Dates are written as-is (the repository normalizes
    /// on write; direct seeding lets tests pin the exact whole-day UTC window they need).
    /// </summary>
    protected async Task<Event> SeedEventAsync(
        ulong userId,
        string name,
        DateTime startDate,
        DateTime endDate,
        bool closed = false,
        string? description = null)
    {
        await using var context = CreateContext();
        var evt = new Event
        {
            UserId = userId,
            Name = name,
            Description = description,
            StartDate = startDate,
            EndDate = endDate,
            IsClosed = closed,
            ClosedAt = closed ? DateTime.UtcNow : null
        };
        context.Events.Add(evt);
        await context.SaveChangesAsync();
        return evt;
    }

    protected async Task<Event?> ReloadEventAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.Events.AsNoTracking().FirstOrDefaultAsync(evt => evt.Uuid == uuid);
    }

    /// <summary>Seeds a full ledger: a user, its owner-representative member, and a default category.</summary>
    protected async Task<Ledger> SeedLedgerAsync()
    {
        var user = await SeedUserAsync();
        var ownerRep = await SeedMemberAsync(user.Id, "Tôi", ownerRep: true);
        var defaultCategory = await SeedCategoryAsync(user.Id, "Ăn uống", isDefault: true);
        return new Ledger(user, ownerRep, defaultCategory);
    }

    protected async Task<Member> SeedMemberAsync(ulong userId, string name, bool ownerRep = false, bool deleted = false)
    {
        await using var context = CreateContext();
        var member = new Member { UserId = userId, Name = name, IsOwnerRepresentative = ownerRep, IsDeleted = deleted };
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    protected async Task<Category> SeedCategoryAsync(ulong userId, string name, bool isDefault = false, bool deleted = false)
    {
        await using var context = CreateContext();
        var category = new Category { UserId = userId, Name = name, Color = DefaultColor, IsDefault = isDefault, IsDeleted = deleted };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    protected async Task<Tag> SeedTagAsync(ulong userId, string name, bool deleted = false)
    {
        await using var context = CreateContext();
        var tag = new Tag { UserId = userId, Name = name, IsDeleted = deleted };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag;
    }

    protected async Task<Expense?> ReloadExpenseAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.Expenses.AsNoTracking()
            .Include(expense => expense.Shares)
            .Include(expense => expense.ExpenseTags)
            .FirstOrDefaultAsync(expense => expense.Uuid == uuid);
    }

    protected async Task<int> CountSharesAsync(ulong expenseId)
    {
        await using var context = CreateContext();
        return await context.Shares.CountAsync(share => share.ExpenseId == expenseId);
    }

    public override async Task DisposeAsync()
    {
        if (!Fixture.IsAvailable)
        {
            await base.DisposeAsync();
            return;
        }

        await using (var context = CreateContext())
        {
            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();

            // Delete expenses FIRST: their RESTRICT FKs to categories/members would otherwise block
            // the base class's user-cascade delete. This cascades shares + expense_tags. (Deleting an
            // expense clears the event_id SetNull link too.)
            await context.Expenses.Where(expense => userIds.Contains(expense.UserId)).ExecuteDeleteAsync();

            // Sweep events by the prefix's owner. The events.user_id FK cascades on user delete, so the
            // base class's user-cascade already clears them; this explicit hard-delete sweep guarantees
            // no event row can survive a run (events are hard-deleted, not soft, M6/OQ3).
            await context.Events.Where(evt => userIds.Contains(evt.UserId)).ExecuteDeleteAsync();

            // Sweep audit_logs by the prefix's actor. The actor_user_id FK also cascades on user
            // delete, but entity_uuid/expense_uuid carry NO FK, so this explicit sweep guarantees no
            // orphan history row can survive a run.
            await context.AuditLogs.Where(log => userIds.Contains(log.ActorUserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>A seeded ledger: the user plus its always-present owner-rep member and default category.</summary>
    protected sealed record Ledger(User User, Member OwnerRep, Category DefaultCategory);
}
