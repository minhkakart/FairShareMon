using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) for the three M10 DB-side count methods that back the
/// Free-tier create-limits: <see cref="MemberRepository.CountActiveByUserAsync"/> (active members incl.
/// owner-rep, excludes soft-deleted, user-scoped), <see cref="EventRepository.CountOpenByUserAsync"/>
/// (<c>is_closed = false</c> only, user-scoped), and
/// <see cref="ExpenseRepository.CountByUserInRangeAsync"/> (<c>expense_time</c> in the half-open UTC
/// window <c>[from, to)</c>, user-scoped). Proves the §4.9 "frees a slot" behaviours (soft-delete a
/// member, close an event) and that another user's rows are never counted.
/// </summary>
[Collection("AuthIntegration")]
public class TierCountRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private MemberRepository CreateMemberRepository() => new(CreateContext());

    private async Task SetMemberDeletedAsync(ulong memberId, bool deleted)
    {
        await using var context = CreateContext();
        var member = await context.Members.IgnoreQueryFilters().FirstAsync(m => m.Id == memberId);
        member.IsDeleted = deleted;
        await context.SaveChangesAsync();
    }

    private async Task SetEventClosedAsync(ulong eventId, bool closed)
    {
        await using var context = CreateContext();
        var evt = await context.Events.FirstAsync(e => e.Id == eventId);
        evt.IsClosed = closed;
        evt.ClosedAt = closed ? DateTime.UtcNow : null;
        await context.SaveChangesAsync();
    }

    private async Task<Expense> SeedExpenseAsync(ulong userId, ulong payerMemberId, ulong categoryId, DateTime expenseTime)
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId,
            Name = "Chi tiêu",
            ExpenseTime = expenseTime,
            PayerMemberId = payerMemberId,
            CategoryId = categoryId
        };
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense;
    }

    // ---- Members ----------------------------------------------------------------------------------

    [SkippableFact]
    public async Task CountActiveByUserAsync_CountsActiveInclOwnerRep_ExcludesSoftDeleted_UserScoped()
    {
        Fixture.SkipIfNoDb();
        var ledger = await SeedLedgerAsync();                       // user + owner-rep (1 active)
        await SeedMemberAsync(ledger.User.Id, "An");                // +1 active
        await SeedMemberAsync(ledger.User.Id, "Bình");              // +1 active
        await SeedMemberAsync(ledger.User.Id, "Đã xóa", deleted: true); // soft-deleted -> not counted

        // Another user's members must not leak into the count.
        var other = await SeedUserAsync();
        await SeedMemberAsync(other.Id, "Người lạ");

        var count = await CreateMemberRepository().CountActiveByUserAsync(ledger.User.Uuid);

        Assert.Equal(3, count); // owner-rep + An + Bình; the soft-deleted and the stranger excluded
    }

    [SkippableFact]
    public async Task CountActiveByUserAsync_SoftDeletingAMember_FreesASlot()
    {
        Fixture.SkipIfNoDb();
        var ledger = await SeedLedgerAsync();
        var member = await SeedMemberAsync(ledger.User.Id, "An");

        var repository = CreateMemberRepository();
        Assert.Equal(2, await repository.CountActiveByUserAsync(ledger.User.Uuid)); // owner-rep + An

        await SetMemberDeletedAsync(member.Id, deleted: true);

        Assert.Equal(1, await CreateMemberRepository().CountActiveByUserAsync(ledger.User.Uuid)); // slot freed
    }

    // ---- Open events ------------------------------------------------------------------------------

    [SkippableFact]
    public async Task CountOpenByUserAsync_CountsOnlyOpen_UserScoped()
    {
        Fixture.SkipIfNoDb();
        var user = await SeedUserAsync();
        await SeedEventAsync(user.Id, "Đà Lạt", new DateTime(2026, 7, 1), new DateTime(2026, 7, 5));
        await SeedEventAsync(user.Id, "Sa Pa", new DateTime(2026, 7, 10), new DateTime(2026, 7, 12));
        await SeedEventAsync(user.Id, "Đã chốt", new DateTime(2026, 6, 1), new DateTime(2026, 6, 3), closed: true);

        var other = await SeedUserAsync();
        await SeedEventAsync(other.Id, "Của người khác", new DateTime(2026, 7, 1), new DateTime(2026, 7, 2));

        var count = await CreateEventRepository().CountOpenByUserAsync(user.Uuid);

        Assert.Equal(2, count); // the two open events; the closed one and the stranger's excluded
    }

    [SkippableFact]
    public async Task CountOpenByUserAsync_ClosingAnEvent_FreesASlot()
    {
        Fixture.SkipIfNoDb();
        var user = await SeedUserAsync();
        var evt = await SeedEventAsync(user.Id, "Đà Lạt", new DateTime(2026, 7, 1), new DateTime(2026, 7, 5));

        Assert.Equal(1, await CreateEventRepository().CountOpenByUserAsync(user.Uuid));

        await SetEventClosedAsync(evt.Id, closed: true);

        Assert.Equal(0, await CreateEventRepository().CountOpenByUserAsync(user.Uuid)); // slot freed
    }

    // ---- Monthly expenses -------------------------------------------------------------------------

    [SkippableFact]
    public async Task CountByUserInRangeAsync_CountsOnlyExpenseTimeInHalfOpenWindow_UserScoped()
    {
        Fixture.SkipIfNoDb();
        var ledger = await SeedLedgerAsync();
        var userId = ledger.User.Id;
        var payer = ledger.OwnerRep.Id;
        var category = ledger.DefaultCategory.Id;

        // Window = July 2026 UTC: [2026-07-01T00:00, 2026-08-01T00:00).
        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedExpenseAsync(userId, payer, category, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)); // in
        await SeedExpenseAsync(userId, payer, category, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));   // in (inclusive start)
        await SeedExpenseAsync(userId, payer, category, new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc)); // in
        await SeedExpenseAsync(userId, payer, category, new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)); // out (last month)
        await SeedExpenseAsync(userId, payer, category, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));   // out (exclusive end -> next month)

        // Another user, dated inside the window -> must not be counted.
        var other = await SeedLedgerAsync();
        await SeedExpenseAsync(other.User.Id, other.OwnerRep.Id, other.DefaultCategory.Id, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));

        var count = await CreateExpenseRepository().CountByUserInRangeAsync(ledger.User.Uuid, from, to);

        Assert.Equal(3, count); // the three July rows; last-month, next-month, and the stranger excluded
    }
}
