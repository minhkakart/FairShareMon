using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the settled-per-member Layer A reconciliation against the real MariaDB
/// (skippable): <c>ShareRepository.SetSettledAsync</c> (per-share toggle + whole-expense reconcile) and
/// <c>ExpenseRepository.SetSettledAsync</c> (whole-expense toggle cascading to its billable shares), plus
/// the migration's data-backfill SQL. Proves the OQ3a predicate (billable = amount &gt; 0 and member ≠
/// payer), OQ6a (payer-own + 0đ shares are settled-by-definition and untouched by the cascade), OQ5a
/// (allowed on a CLOSED event), OQ10a (no audit), that no amount is ever changed (§4.3), and the
/// resource-owned 404 semantics (never the row). All expenses/shares are seeded directly so the exact
/// payer / share-set / event / lifecycle is pinned before the real repository runs.
/// </summary>
[Collection("AuthIntegration")]
public class SettledReconciliationRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Seeds an expense + shares directly (no repository) so the payer / share-set / event / settled state are pinned.</summary>
    private async Task<Expense> SeedExpenseAsync(
        ulong userId,
        ulong payerMemberId,
        ulong categoryId,
        ulong? eventId,
        bool settled,
        params (ulong MemberId, decimal Amount)[] shares)
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId,
            Name = "Chi tiêu",
            ExpenseTime = Noon,
            PayerMemberId = payerMemberId,
            CategoryId = categoryId,
            EventId = eventId,
            IsSettled = settled,
            SettledAt = settled ? new DateTime(2026, 7, 10, 8, 30, 0, DateTimeKind.Utc) : null
        };
        foreach (var (memberId, amount) in shares)
            expense.Shares.Add(new Share { MemberId = memberId, Amount = amount });
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense;
    }

    private async Task<Expense> ReloadWithSharesAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.Expenses.AsNoTracking()
            .Include(expense => expense.Shares)
            .SingleAsync(expense => expense.Uuid == uuid);
    }

    private async Task<Share> ShareForMemberAsync(ulong expenseId, ulong memberId)
    {
        await using var context = CreateContext();
        return await context.Shares.AsNoTracking().SingleAsync(share => share.ExpenseId == expenseId && share.MemberId == memberId);
    }

    // ============================ ShareRepository.SetSettledAsync (Layer A) ============================

    [SkippableFact]
    public async Task SetSettledAsync_TogglesShareFlagAndSettledAt()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));
        var binhShare = await ShareForMemberAsync(expense.Id, binh.Id);

        var status = await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, binhShare.Uuid, isSettled: true);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        var persisted = await ShareForMemberAsync(expense.Id, binh.Id);
        Assert.True(persisted.IsSettled);
        Assert.NotNull(persisted.SettledAt);
    }

    [SkippableFact]
    public async Task SetSettledAsync_AllBillableSharesSettled_FlipsExpenseSettledTrueThenFalse()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        // Payer = owner-rep; billable share-set is just Bình's 100k (owner-rep 0đ is non-billable).
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));
        var binhShare = await ShareForMemberAsync(expense.Id, binh.Id);

        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, binhShare.Uuid, isSettled: true);
        var afterSettle = await ReloadWithSharesAsync(expense.Uuid);
        Assert.True(afterSettle.IsSettled);          // all billable shares settled ⇒ expense settled (OQ3a)
        Assert.NotNull(afterSettle.SettledAt);

        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, binhShare.Uuid, isSettled: false);
        var afterUnsettle = await ReloadWithSharesAsync(expense.Uuid);
        Assert.False(afterUnsettle.IsSettled);        // unsettling one billable share flips it back
        Assert.Null(afterUnsettle.SettledAt);
    }

    [SkippableFact]
    public async Task SetSettledAsync_OnlyPayerOwnAndZeroShares_ReconcilesToSettledByDefinition()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        // Payer = Bình; shares are the owner-rep 0đ (0đ) + Bình's own 500k (member == payer). NO billable shares.
        var expense = await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));
        var ownerRepShare = await ShareForMemberAsync(expense.Id, ledger.OwnerRep.Id);

        // Toggling the 0đ share to FALSE still reconciles the expense to settled: with no billable shares
        // the "all billable settled" predicate is vacuously true (payer-own + 0đ are settled-by-definition, OQ6a).
        var status = await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, ownerRepShare.Uuid, isSettled: false);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        var reloaded = await ReloadWithSharesAsync(expense.Uuid);
        Assert.True(reloaded.IsSettled);
    }

    [SkippableFact]
    public async Task SetSettledAsync_WritesNoAuditRowAndDoesNotChangeAmount()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));
        var binhShare = await ShareForMemberAsync(expense.Id, binh.Id);

        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, binhShare.Uuid, isSettled: true);

        await using var context = CreateContext();
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.ActorUserId == ledger.User.Id)); // no audit (OQ10a)
        Assert.Equal(100_000m, (await context.Shares.AsNoTracking().SingleAsync(s => s.Id == binhShare.Id)).Amount); // amount untouched (§4.3)
    }

    [SkippableFact]
    public async Task SetSettledAsync_ClosedEventExpense_Succeeds()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var closedEvent = await SeedEventAsync(ledger.User.Id, "Chốt", Day14, Day16, closed: true);
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, closedEvent.Id, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));
        var binhShare = await ShareForMemberAsync(expense.Id, binh.Id);

        var status = await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, binhShare.Uuid, isSettled: true);

        Assert.Equal(ExpenseWriteStatus.Success, status); // §4.4 sole exception - not guarded on a closed event (OQ5a)
    }

    [SkippableFact]
    public async Task SetSettledAsync_AnotherUsersExpense_ReturnsExpenseNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));
        var binhShare = await ShareForMemberAsync(expense.Id, binh.Id);

        var status = await CreateShareRepository().SetSettledAsync(stranger.User.Uuid, expense.Uuid, binhShare.Uuid, isSettled: true);

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, status); // resource-owned: existence not leaked
        Assert.False((await ShareForMemberAsync(expense.Id, binh.Id)).IsSettled); // untouched
    }

    [SkippableFact]
    public async Task SetSettledAsync_UnknownShare_ReturnsShareNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));

        var status = await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, "no-such-share", isSettled: true);

        Assert.Equal(ExpenseWriteStatus.ShareNotFound, status);
    }

    // ==================== ExpenseRepository.SetSettledAsync (whole-expense cascade) ====================

    [SkippableFact]
    public async Task ExpenseSetSettled_True_CascadesToBillableSharesOnly()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        // Payer = Bình. Billable: Cường 300k + owner-rep 200k. Non-billable: Bình's own 100k + a 0đ share (none here).
        var expense = await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 200_000m), (binh.Id, 100_000m), (cuong.Id, 300_000m));

        var status = await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, isSettled: true);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        var reloaded = await ReloadWithSharesAsync(expense.Uuid);
        Assert.True(reloaded.IsSettled);
        Assert.NotNull(reloaded.SettledAt);
        // Billable shares (owner-rep 200k, Cường 300k) are settled; the payer's own 100k share is left untouched (OQ6a).
        Assert.True(reloaded.Shares.Single(s => s.MemberId == ledger.OwnerRep.Id).IsSettled);
        Assert.True(reloaded.Shares.Single(s => s.MemberId == cuong.Id).IsSettled);
        Assert.False(reloaded.Shares.Single(s => s.MemberId == binh.Id).IsSettled); // payer-own, non-billable
    }

    [SkippableFact]
    public async Task ExpenseSetSettled_False_ClearsBillableShares()
    {
        var ledger = await SeedLedgerAsync();
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (cuong.Id, 300_000m));

        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, isSettled: true);
        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, isSettled: false);

        var reloaded = await ReloadWithSharesAsync(expense.Uuid);
        Assert.False(reloaded.IsSettled);
        Assert.Null(reloaded.SettledAt);
        var cuongShare = reloaded.Shares.Single(s => s.MemberId == cuong.Id);
        Assert.False(cuongShare.IsSettled);
        Assert.Null(cuongShare.SettledAt);
    }

    [SkippableFact]
    public async Task ExpenseSetSettled_ClosedEvent_SucceedsWithNoAudit()
    {
        var ledger = await SeedLedgerAsync();
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var closedEvent = await SeedEventAsync(ledger.User.Id, "Chốt", Day14, Day16, closed: true);
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, closedEvent.Id, settled: false,
            (ledger.OwnerRep.Id, 0m), (cuong.Id, 300_000m));

        var status = await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, isSettled: true);

        Assert.Equal(ExpenseWriteStatus.Success, status); // no closed-event guard (§4.4 exception, OQ13)
        await using var context = CreateContext();
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.ActorUserId == ledger.User.Id)); // no audit (OQ11)
    }

    [SkippableFact]
    public async Task ExpenseSetSettled_AnotherUsersExpense_ReturnsExpenseNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var expense = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, eventId: null, settled: false,
            (ledger.OwnerRep.Id, 0m), (cuong.Id, 300_000m));

        var status = await CreateExpenseRepository().SetSettledAsync(stranger.User.Uuid, expense.Uuid, isSettled: true);

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, status);
        Assert.False((await ReloadWithSharesAsync(expense.Uuid)).IsSettled); // untouched
    }

    // ============================ Migration data backfill (OQ4a) ============================

    /// <summary>
    /// Reproduces the <c>AddPerMemberSettlement</c> migration's data step (scoped to the seeded user so a
    /// shared DB is never disturbed): every share of an already-settled expense becomes settled with the
    /// expense's <c>settled_at</c>, and NO Layer B (<c>event_member_settlements</c>) rows are fabricated.
    /// Confirms the invariant the migration guarantees from day one for pre-existing data.
    /// </summary>
    [SkippableFact]
    public async Task MigrationBackfill_SettledExpenseShares_BecomeSettledWithExpenseSettledAt_NoLayerB()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt cũ", Day14, Day16);
        // A pre-migration already-settled expense: is_settled = true but its shares were never per-share flagged.
        var settled = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, settled: true,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 100_000m));
        // An unsettled expense must stay untouched by the backfill.
        var unsettled = await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, settled: false,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 50_000m));

        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                UPDATE shares s
                JOIN expenses e ON e.id = s.expense_id
                SET s.is_settled = 1, s.settled_at = e.settled_at
                WHERE e.is_settled = 1 AND e.user_id = {0};
                """,
                ledger.User.Id);
        }

        var settledReloaded = await ReloadWithSharesAsync(settled.Uuid);
        Assert.All(settledReloaded.Shares, share => Assert.True(share.IsSettled)); // ALL shares of the settled expense
        Assert.All(settledReloaded.Shares, share => Assert.Equal(settledReloaded.SettledAt, share.SettledAt));

        var unsettledReloaded = await ReloadWithSharesAsync(unsettled.Uuid);
        Assert.All(unsettledReloaded.Shares, share => Assert.False(share.IsSettled)); // untouched

        await using (var context = CreateContext())
            Assert.Equal(0, await context.EventMemberSettlements.CountAsync(s => s.EventId == evt.Id)); // no Layer B backfill
    }
}
