using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Stats;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>StatsRepository</c> against the real MariaDB (skippable). Covers the §3.7
/// per-event balance (the canonical scenario, the sum-to-zero invariant with exact decimals incl. cents,
/// owner-rep-at-0đ inclusion, soft-deleted-member inclusion, settled-neutrality, loose-expense
/// exclusion, open AND closed events, owned-but-empty → empty rows, resource-owned scoping) and the §3.9
/// overview + by-category aggregates (loose+event totals, inclusive UTC boundaries, all-time default,
/// empty → zeros, per-category grouping with deleted-category inclusion, total-DESC sort, category
/// totals reconciling to the overview total, and per-user isolation). Datasets are deliberately
/// multi-member / multi-category / multi-expense so a client-eval aggregation bug would surface.
/// </summary>
[Collection("AuthIntegration")]
public class StatsRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private StatsRepository CreateStatsRepository() => new(CreateContext());

    /// <summary>Seeds an expense with its shares directly (no repository), so tests pin the exact payer/shares/event/time they need.</summary>
    private async Task<Expense> SeedExpenseAsync(
        ulong userId,
        ulong payerMemberId,
        ulong categoryId,
        DateTime expenseTime,
        ulong? eventId,
        IEnumerable<(ulong MemberId, decimal Amount)> shares,
        bool settled = false,
        string name = "Chi tiêu")
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId,
            Name = name,
            ExpenseTime = expenseTime,
            PayerMemberId = payerMemberId,
            CategoryId = categoryId,
            EventId = eventId,
            IsSettled = settled,
            SettledAt = settled ? DateTime.UtcNow : null
        };
        foreach (var (memberId, amount) in shares)
            expense.Shares.Add(new Share { MemberId = memberId, Amount = amount });
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense;
    }

    private async Task SetSettledAsync(ulong expenseId, bool settled)
    {
        await using var context = CreateContext();
        var expense = await context.Expenses.FirstAsync(exp => exp.Id == expenseId);
        expense.IsSettled = settled;
        expense.SettledAt = settled ? DateTime.UtcNow : null;
        await context.SaveChangesAsync();
    }

    private static MemberBalanceAggregate Row(IReadOnlyList<MemberBalanceAggregate> rows, string uuid) =>
        Assert.Single(rows, row => row.MemberUuid == uuid);

    // ============================ Balance (§3.7) ============================

    [SkippableFact]
    public async Task GetEventBalanceAsync_CanonicalScenario_ComputesAdvancedOwedAndSumsToZero()
    {
        // §3.7 scenario: everyone owes 500k; Bình advanced 800k (+300k), An advanced 700k (+200k),
        // Cường advanced 0 (−500k). Σ balance == 0.
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);

        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 300_000m), (binh.Id, 200_000m), (cuong.Id, 300_000m)]); // Bình advanced 800k
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 200_000m), (binh.Id, 300_000m), (cuong.Id, 200_000m)]); // An advanced 700k

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        Assert.Equal(3, rows.Count);

        var an = Row(rows, ledger.OwnerRep.Uuid);
        Assert.Equal(700_000m, an.Advanced);
        Assert.Equal(500_000m, an.Owed);
        Assert.Equal(200_000m, an.Advanced - an.Owed);
        Assert.True(an.IsOwnerRepresentative);

        var binhRow = Row(rows, binh.Uuid);
        Assert.Equal(800_000m, binhRow.Advanced);
        Assert.Equal(500_000m, binhRow.Owed);
        Assert.Equal(300_000m, binhRow.Advanced - binhRow.Owed);

        var cuongRow = Row(rows, cuong.Uuid);
        Assert.Equal(0m, cuongRow.Advanced);
        Assert.Equal(500_000m, cuongRow.Owed);
        Assert.Equal(-500_000m, cuongRow.Advanced - cuongRow.Owed);

        Assert.Equal(0m, rows.Sum(row => row.Advanced - row.Owed)); // sum-to-zero, exact
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_FractionalCents_SumsToExactlyZeroNoDrift()
    {
        // Odd split that does not divide evenly - the balances must still sum to EXACTLY 0m (decimal).
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Ăn vặt", Day14, Day16);

        // 1000.00 split three ways with cents.
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 333.33m), (binh.Id, 333.33m), (cuong.Id, 333.34m)]);
        await SeedExpenseAsync(ledger.User.Id, cuong.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 0.01m), (binh.Id, 0.02m), (cuong.Id, 0.03m)]);

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        Assert.Equal(0m, rows.Sum(row => row.Advanced - row.Owed));
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_OwnerRepWithZeroShare_AppearsAtZero_NonParticipantOmitted()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường"); // never participates
        var evt = await SeedEventAsync(ledger.User.Id, "Cà phê", Day14, Day16);

        // Owner-rep only carries a 0đ share; Bình pays and bears everything.
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m)]);

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        Assert.Equal(2, rows.Count);
        var an = Row(rows, ledger.OwnerRep.Uuid);
        Assert.Equal(0m, an.Advanced);
        Assert.Equal(0m, an.Owed); // owner-rep participates (0đ) → appears (OQ3)
        Assert.DoesNotContain(rows, row => row.MemberUuid == cuong.Uuid); // non-participant omitted
        Assert.Equal(0m, rows.Sum(row => row.Advanced - row.Owed));
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_SoftDeletedMember_StillAppearsWithFiguresAndSumZero()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình", deleted: true); // soft-deleted participant
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt cũ", Day14, Day16);

        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 400_000m), (binh.Id, 200_000m)]); // Bình advanced 600k

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        var binhRow = Row(rows, binh.Uuid);
        Assert.True(binhRow.IsDeleted); // §4.7 - deleted member still in the historical balance
        Assert.Equal(600_000m, binhRow.Advanced);
        Assert.Equal(200_000m, binhRow.Owed);
        Assert.Equal(400_000m, binhRow.Advanced - binhRow.Owed);
        Assert.Equal(0m, rows.Sum(row => row.Advanced - row.Owed));
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_SettledToggle_LeavesBalanceIdentical()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt", Day14, Day16);
        var expense = await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 300_000m), (binh.Id, 100_000m)]);

        var before = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        await SetSettledAsync(expense.Id, true); // toggle payment metadata
        var after = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        // Balance ignores is_settled entirely (OQ2): identical advanced/owed per member.
        foreach (var row in before)
        {
            var match = Row(after, row.MemberUuid);
            Assert.Equal(row.Advanced, match.Advanced);
            Assert.Equal(row.Owed, match.Owed);
        }
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_LooseExpense_ExcludedFromBalance()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt", Day14, Day16);
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 300_000m), (binh.Id, 100_000m)]); // in-event
        // A loose expense (event_id null) by/for the same members - must NOT affect the event balance.
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 999_000m), (binh.Id, 999_000m)]);

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        var an = Row(rows, ledger.OwnerRep.Uuid);
        Assert.Equal(0m, an.Advanced); // loose expense's 999k not counted
        Assert.Equal(300_000m, an.Owed);
        var binhRow = Row(rows, binh.Uuid);
        Assert.Equal(400_000m, binhRow.Advanced); // only the in-event 300k+100k
        Assert.Equal(0m, rows.Sum(row => row.Advanced - row.Owed));
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_OpenAndClosedEvents_BothReturnBalance()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var openEvt = await SeedEventAsync(ledger.User.Id, "Mở", Day14, Day16, closed: false);
        var closedEvt = await SeedEventAsync(ledger.User.Id, "Chốt", Day14, Day16, closed: true);
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, openEvt.Id,
            [(ledger.OwnerRep.Id, 100_000m), (binh.Id, 100_000m)]);
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, closedEvt.Id,
            [(ledger.OwnerRep.Id, 100_000m), (binh.Id, 100_000m)]);

        var openRows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, openEvt.Id);
        var closedRows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, closedEvt.Id);

        Assert.NotEmpty(openRows); // OQ4 - both lifecycles
        Assert.NotEmpty(closedRows);
        Assert.Equal(0m, closedRows.Sum(row => row.Advanced - row.Owed));
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_OwnedEmptyEvent_ReturnsEmptyRows()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await SeedEventAsync(ledger.User.Id, "Trống", Day14, Day16);

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        Assert.Empty(rows); // OQ15
    }

    [SkippableFact]
    public async Task FindOwnedEventAsync_AnotherUsersEvent_ReturnsNull()
    {
        var owner = await SeedLedgerAsync();
        var stranger = await SeedUserAsync();
        var evt = await SeedEventAsync(owner.User.Id, "Của tôi", Day14, Day16);

        var seenByStranger = await CreateStatsRepository().FindOwnedEventAsync(stranger.Uuid, evt.Uuid);
        var seenByOwner = await CreateStatsRepository().FindOwnedEventAsync(owner.User.Uuid, evt.Uuid);

        Assert.Null(seenByStranger); // resource-owned: existence not leaked
        Assert.NotNull(seenByOwner);
    }

    // ============================ Overview (§3.9) ============================

    [SkippableFact]
    public async Task GetOverviewAsync_SumsSharesAcrossLooseAndEventExpensesInRange()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt", Day14, Day16);

        // In range: one event expense (300k) + one loose expense (200k).
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 100_000m), (binh.Id, 200_000m)]);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 200_000m)]);
        // Out of range (later).
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, Day16.AddDays(5), eventId: null,
            [(binh.Id, 999_000m)]);

        var inRange = await CreateStatsRepository().GetOverviewAsync(ledger.User.Uuid, Day14, Day16.AddDays(1));

        Assert.Equal(500_000m, inRange.TotalSpending); // loose + event, out-of-range excluded
        Assert.Equal(2, inRange.ExpenseCount);
    }

    [SkippableFact]
    public async Task GetOverviewAsync_NoBounds_CoversAllTime()
    {
        var ledger = await SeedLedgerAsync();
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)]);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day16.AddYears(1), eventId: null,
            [(ledger.OwnerRep.Id, 400_000m)]);

        var allTime = await CreateStatsRepository().GetOverviewAsync(ledger.User.Uuid, from: null, to: null);

        Assert.Equal(500_000m, allTime.TotalSpending);
        Assert.Equal(2, allTime.ExpenseCount);
    }

    [SkippableFact]
    public async Task GetOverviewAsync_InclusiveBoundaries_CountsEndpointsExcludesJustPast()
    {
        var ledger = await SeedLedgerAsync();
        var from = new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 16, 20, 0, 0, DateTimeKind.Utc);

        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, from, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)], name: "Tại from");
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, to, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)], name: "Tại to");
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, to.AddTicks(10), eventId: null,
            [(ledger.OwnerRep.Id, 999_000m)], name: "1 micro sau to"); // 1µs past to → excluded

        var overview = await CreateStatsRepository().GetOverviewAsync(ledger.User.Uuid, from, to);

        Assert.Equal(200_000m, overview.TotalSpending); // both endpoints counted, the +1µs one excluded
        Assert.Equal(2, overview.ExpenseCount);
    }

    [SkippableFact]
    public async Task GetOverviewAsync_EmptyRange_ReturnsZeros()
    {
        var ledger = await SeedLedgerAsync();
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)]);

        var empty = await CreateStatsRepository().GetOverviewAsync(ledger.User.Uuid, Day16.AddDays(10), Day16.AddDays(20));

        Assert.Equal(0m, empty.TotalSpending);
        Assert.Equal(0, empty.ExpenseCount);
    }

    [SkippableFact]
    public async Task GetOverviewAsync_IsPerUserIsolated()
    {
        var owner = await SeedLedgerAsync();
        var other = await SeedLedgerAsync();
        await SeedExpenseAsync(owner.User.Id, owner.OwnerRep.Id, owner.DefaultCategory.Id, Day15Noon, eventId: null,
            [(owner.OwnerRep.Id, 100_000m)]);
        await SeedExpenseAsync(other.User.Id, other.OwnerRep.Id, other.DefaultCategory.Id, Day15Noon, eventId: null,
            [(other.OwnerRep.Id, 777_000m)]); // must never count for owner

        var overview = await CreateStatsRepository().GetOverviewAsync(owner.User.Uuid, from: null, to: null);

        Assert.Equal(100_000m, overview.TotalSpending);
        Assert.Equal(1, overview.ExpenseCount);
    }

    // ============================ By-category (§3.9) ============================

    [SkippableFact]
    public async Task GetByCategoryAsync_TimeRange_GroupsTotalsAndCounts_SortedTotalDesc()
    {
        var ledger = await SeedLedgerAsync(); // default category "Ăn uống"
        var travel = await SeedCategoryAsync(ledger.User.Id, "Di chuyển");
        var stay = await SeedCategoryAsync(ledger.User.Id, "Lưu trú");
        var unused = await SeedCategoryAsync(ledger.User.Id, "Không dùng"); // zero-expense → omitted

        // Ăn uống: 2 expenses totalling 300k; Di chuyển: 1 expense 500k; Lưu trú: 1 expense 100k.
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)]);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 200_000m)]);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, travel.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 500_000m)]);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, stay.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)]);

        var rows = await CreateStatsRepository().GetByCategoryAsync(ledger.User.Uuid, Day14, Day16.AddDays(1), eventId: null);

        Assert.Equal(3, rows.Count); // "Không dùng" omitted (no in-scope expense, OQ9)
        Assert.DoesNotContain(rows, row => row.CategoryUuid == unused.Uuid);

        // Sorted total DESC: Di chuyển 500k, Ăn uống 300k, Lưu trú 100k.
        Assert.Equal([travel.Uuid, ledger.DefaultCategory.Uuid, stay.Uuid], rows.Select(row => row.CategoryUuid));

        var food = Assert.Single(rows, row => row.CategoryUuid == ledger.DefaultCategory.Uuid);
        Assert.Equal(300_000m, food.Total);
        Assert.Equal(2, food.ExpenseCount);
        Assert.Equal(DefaultColor, food.Color); // color carried for charts
    }

    [SkippableFact]
    public async Task GetByCategoryAsync_DeletedCategoryWithHistory_StillAppears()
    {
        var ledger = await SeedLedgerAsync();
        var deleted = await SeedCategoryAsync(ledger.User.Id, "Đã xóa", deleted: true);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, deleted.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 250_000m)]);

        var rows = await CreateStatsRepository().GetByCategoryAsync(ledger.User.Uuid, Day14, Day16.AddDays(1), eventId: null);

        var row = Assert.Single(rows, r => r.CategoryUuid == deleted.Uuid);
        Assert.True(row.IsDeleted); // §4.7 - deleted category with historical spend still shown
        Assert.Equal(250_000m, row.Total);
        Assert.Equal(1, row.ExpenseCount);
    }

    [SkippableFact]
    public async Task GetByCategoryAsync_EventMode_ScopesToThatEventOnly()
    {
        var ledger = await SeedLedgerAsync();
        var travel = await SeedCategoryAsync(ledger.User.Id, "Di chuyển");
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt", Day14, Day16);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, evt.Id,
            [(ledger.OwnerRep.Id, 400_000m)]);
        // A loose expense in a different category - must NOT show in the event-mode result.
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, travel.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 999_000m)]);

        var rows = await CreateStatsRepository().GetByCategoryAsync(ledger.User.Uuid, from: null, to: null, eventId: evt.Id);

        var row = Assert.Single(rows);
        Assert.Equal(ledger.DefaultCategory.Uuid, row.CategoryUuid);
        Assert.Equal(400_000m, row.Total);
    }

    [SkippableFact]
    public async Task GetByCategoryAsync_CategoryTotals_ReconcileToOverviewTotalForSameRange()
    {
        var ledger = await SeedLedgerAsync();
        var travel = await SeedCategoryAsync(ledger.User.Id, "Di chuyển");
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 120_000m)]);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, travel.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 80_000m)]);

        var byCategory = await CreateStatsRepository().GetByCategoryAsync(ledger.User.Uuid, Day14, Day16.AddDays(1), eventId: null);
        var overview = await CreateStatsRepository().GetOverviewAsync(ledger.User.Uuid, Day14, Day16.AddDays(1));

        Assert.Equal(overview.TotalSpending, byCategory.Sum(row => row.Total)); // 200k both ways
    }

    [SkippableFact]
    public async Task GetByCategoryAsync_EmptyScope_ReturnsEmptyList()
    {
        var ledger = await SeedLedgerAsync();
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, Day15Noon, eventId: null,
            [(ledger.OwnerRep.Id, 100_000m)]);

        var rows = await CreateStatsRepository().GetByCategoryAsync(ledger.User.Uuid, Day16.AddDays(10), Day16.AddDays(20), eventId: null);

        Assert.Empty(rows);
    }

    [SkippableFact]
    public async Task GetByCategoryAsync_IsPerUserIsolated()
    {
        var owner = await SeedLedgerAsync();
        var other = await SeedLedgerAsync();
        await SeedExpenseAsync(owner.User.Id, owner.OwnerRep.Id, owner.DefaultCategory.Id, Day15Noon, eventId: null,
            [(owner.OwnerRep.Id, 100_000m)]);
        await SeedExpenseAsync(other.User.Id, other.OwnerRep.Id, other.DefaultCategory.Id, Day15Noon, eventId: null,
            [(other.OwnerRep.Id, 777_000m)]);

        var rows = await CreateStatsRepository().GetByCategoryAsync(owner.User.Uuid, Day14, Day16.AddDays(1), eventId: null);

        var row = Assert.Single(rows);
        Assert.Equal(100_000m, row.Total); // other user's category spend never leaks
    }
}
