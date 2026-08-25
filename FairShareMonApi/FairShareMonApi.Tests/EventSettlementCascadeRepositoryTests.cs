using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for event-expense-settlement-sync Milestone 1 (Direction 1: event-level member
/// settle cascades to ALL of that member's shares in the event, gated by
/// <c>EventSettlementClassifier</c> eligibility) against the real MariaDB (skippable). Extends the
/// shipped <see cref="EventMemberSettlementRepositoryTests"/> fixture (which already proves the plain
/// upsert mechanics) with the cascade/reversal behavior layered on top. Per the planning doc's Step
/// M1.5 test list: a single-sided net debtor cascades every share; a gross-pure net creditor cascades;
/// the OQ-L regression (a net creditor who ALSO holds a genuine debtor-share elsewhere is gross-mixed
/// and ineligible - no cascade); the <c>Balance == 0</c> "mixed"/NetZero residual bucket (no cascade);
/// OQ1's unconditional, live-recomputed reversal; closed-event parity (OQ-H); a soft-deleted target
/// (§4.7); no audit row (OQ-G); cross-member isolation (OQ-J); cross-user isolation; and the D2/M7 OQ2
/// byte-for-byte advanced/owed invariant.
/// </summary>
[Collection("AuthIntegration")]
public class EventSettlementCascadeRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private EventMemberSettlementRepository CreateSettlementRepository() => new(CreateContext());

    private StatsRepository CreateStatsRepository() => new(CreateContext());

    /// <summary>Seeds an event expense directly (no repository) so tests pin the exact payer/share membership they need.</summary>
    private async Task<Expense> SeedEventExpenseAsync(ulong userId, ulong payerMemberId, ulong categoryId, ulong eventId, params (ulong MemberId, decimal Amount)[] shares)
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId,
            Name = "Chi tiêu",
            ExpenseTime = Noon,
            PayerMemberId = payerMemberId,
            CategoryId = categoryId,
            EventId = eventId
        };
        foreach (var (memberId, amount) in shares)
            expense.Shares.Add(new Share { MemberId = memberId, Amount = amount });
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense;
    }

    private async Task<Share?> ShareOfAsync(ulong expenseId, ulong memberId)
    {
        await using var context = CreateContext();
        return await context.Shares.AsNoTracking().SingleOrDefaultAsync(share => share.ExpenseId == expenseId && share.MemberId == memberId);
    }

    private async Task<Expense> ReloadExpenseWithSharesAsync(ulong expenseId)
    {
        await using var context = CreateContext();
        return await context.Expenses.AsNoTracking().Include(expense => expense.Shares).SingleAsync(expense => expense.Id == expenseId);
    }

    private async Task<EventMemberSettlement?> SettlementAsync(ulong eventId, ulong memberId)
    {
        await using var context = CreateContext();
        return await context.EventMemberSettlements.AsNoTracking()
            .SingleOrDefaultAsync(settlement => settlement.EventId == eventId && settlement.MemberId == memberId);
    }

    // ============================ 1. Net debtor: full cascade ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_NetDebtor_CascadesAllSharesAndOthersUntouched()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // Expense A: An pays; Bình owes 500k, Cường owes 300k -> Bình is a net debtor.
        var expenseA = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m), (cuong.Id, 300_000m));
        // Expense B: Bình pays for herself only - her own payer-share, a harmless cascade no-op (OQ6a).
        var expenseB = await SeedEventExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, evt.Id,
            (binh.Id, 100_000m));

        var before = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        var binhFactsBefore = Assert.Single(before, row => row.MemberUuid == binh.Uuid);
        Assert.True(binhFactsBefore.IsEligibleForAutoCascade); // net debtor is always eligible

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);
        Assert.Equal(SettlementWriteStatus.Success, status);

        // Bình's shares in BOTH expenses cascaded, including her own payer-own share on B (harmless no-op).
        var binhShareA = await ShareOfAsync(expenseA.Id, binh.Id);
        Assert.True(binhShareA!.IsSettled);
        Assert.NotNull(binhShareA.SettledAt);
        var binhShareB = await ShareOfAsync(expenseB.Id, binh.Id);
        Assert.True(binhShareB!.IsSettled);

        // Cường's share in the SAME expense is untouched (OQ-J cross-member isolation).
        var cuongShareA = await ShareOfAsync(expenseA.Id, cuong.Id);
        Assert.False(cuongShareA!.IsSettled);
        Assert.Null(cuongShareA.SettledAt);

        // Expense A does NOT reconcile to fully settled - Cường's billable share is still unsettled.
        var reloadedA = await ReloadExpenseWithSharesAsync(expenseA.Id);
        Assert.False(reloadedA.IsSettled);

        // Balance figures byte-for-byte unchanged by the cascade (D2/M7 OQ2), mirroring
        // StatsRepositoryTests.GetEventBalanceAsync_SettledToggle_LeavesBalanceIdentical's pattern.
        var after = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        foreach (var row in before)
        {
            var match = Assert.Single(after, r => r.MemberUuid == row.MemberUuid);
            Assert.Equal(row.Advanced, match.Advanced);
            Assert.Equal(row.Owed, match.Owed);
        }
    }

    // ============================ 2. Net creditor, gross-pure ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_NetCreditorGrossPure_CascadeFires()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // An pays 500k, owed 0, holds no debtor-share anywhere in the event -> net creditor, gross-pure.
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        var anFacts = Assert.Single(rows, row => row.MemberUuid == ledger.OwnerRep.Uuid);
        Assert.Equal(500_000m, anFacts.Advanced - anFacts.Owed);
        Assert.True(anFacts.IsEligibleForAutoCascade); // gross-pure net creditor

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, ledger.OwnerRep.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status);
        var anShare = await ShareOfAsync(expense.Id, ledger.OwnerRep.Id);
        Assert.True(anShare!.IsSettled); // cascade fired even though it is only her own 0đ share
    }

    // ============================ 3. OQ-L regression: net creditor, gross-mixed ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_NetCreditorGrossMixed_FlagFlipsButNoCascade()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // Expense X: An pays; Bình owes 300k -> An advances 300k.
        var expenseX = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 300_000m));
        // Expense Y: Cường pays; An holds a GENUINE debtor-share of 200k here.
        var expenseY = await SeedEventExpenseAsync(ledger.User.Id, cuong.Id, ledger.DefaultCategory.Id, evt.Id,
            (cuong.Id, 0m), (ledger.OwnerRep.Id, 200_000m));
        // An: advanced 300k, owed 200k -> balance +100k (net creditor) but holds a debtor-share on Y -> gross-mixed.

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        var anFacts = Assert.Single(rows, row => row.MemberUuid == ledger.OwnerRep.Uuid);
        Assert.Equal(100_000m, anFacts.Advanced - anFacts.Owed);
        Assert.False(anFacts.IsEligibleForAutoCascade); // OQ-L: a gross-mixed creditor is NOT eligible

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, ledger.OwnerRep.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status); // the Layer B flag itself still flips (OQ-A)
        Assert.True((await SettlementAsync(evt.Id, ledger.OwnerRep.Id))!.IsSettled);

        // But NO share is touched - the debtor share on Y stays exactly as it was.
        var anShareY = await ShareOfAsync(expenseY.Id, ledger.OwnerRep.Id);
        Assert.False(anShareY!.IsSettled);
        Assert.Null(anShareY.SettledAt);
        var anShareX = await ShareOfAsync(expenseX.Id, ledger.OwnerRep.Id);
        Assert.False(anShareX!.IsSettled); // her own 0đ payer share on X also untouched - no cascade at all
    }

    // ============================ 4. NetZero ("mixed" residual bucket) ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_NetZeroMember_FlagFlipsButNoCascade()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // Expense P: Bình pays; Cường owes 100k -> Bình advances 100k.
        await SeedEventExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, evt.Id,
            (cuong.Id, 100_000m));
        // Expense Q: An pays; Bình owes 100k -> Bình owed 100k. Balance == 0 exactly.
        var expenseQ = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (binh.Id, 100_000m));

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        var binhFacts = Assert.Single(rows, row => row.MemberUuid == binh.Uuid);
        Assert.Equal(0m, binhFacts.Advanced - binhFacts.Owed);
        Assert.False(binhFacts.IsEligibleForAutoCascade); // NetZero is the ineligible "mixed" bucket

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status);
        Assert.True((await SettlementAsync(evt.Id, binh.Id))!.IsSettled); // flag still flips (OQ-A)
        var binhShareQ = await ShareOfAsync(expenseQ.Id, binh.Id);
        Assert.False(binhShareQ!.IsSettled); // no cascade
    }

    // ============================ 5. Reversal (OQ1): unconditional, recomputed live ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_UnsettleAfterEligibilityChanged_ReversesOriginallyCascadedSharesUnconditionally()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // Expense X: An pays 500k, Bình owes it all. An is (at this point) net creditor, gross-pure.
        var expenseX = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));

        var settlementRepo = CreateSettlementRepository();
        var settleStatus = await settlementRepo.SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, ledger.OwnerRep.Uuid, isSettled: true);
        Assert.Equal(SettlementWriteStatus.Success, settleStatus);
        var anShareXAfterSettle = await ShareOfAsync(expenseX.Id, ledger.OwnerRep.Id);
        Assert.True(anShareXAfterSettle!.IsSettled); // cascaded - gross-pure creditor was eligible at settle time

        // A NEW expense created AFTER the settle gives An a genuine debtor-share -> now gross-MIXED (ineligible).
        var expenseY = await SeedEventExpenseAsync(ledger.User.Id, cuong.Id, ledger.DefaultCategory.Id, evt.Id,
            (cuong.Id, 0m), (ledger.OwnerRep.Id, 200_000m));
        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        var anFactsNow = Assert.Single(rows, row => row.MemberUuid == ledger.OwnerRep.Uuid);
        Assert.False(anFactsNow.IsEligibleForAutoCascade); // classification changed since the original settle

        var unsettleStatus = await settlementRepo.SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, ledger.OwnerRep.Uuid, isSettled: false);

        Assert.Equal(SettlementWriteStatus.Success, unsettleStatus);
        // OQ1: unconditional - the ORIGINALLY-cascaded share on X is still reversed, despite An now being ineligible.
        var anShareXAfterUnsettle = await ShareOfAsync(expenseX.Id, ledger.OwnerRep.Id);
        Assert.False(anShareXAfterUnsettle!.IsSettled);
        Assert.Null(anShareXAfterUnsettle.SettledAt);
        // The new expense's debtor-share is (and stays) unsettled - it was never cascaded either way.
        var anShareY = await ShareOfAsync(expenseY.Id, ledger.OwnerRep.Id);
        Assert.False(anShareY!.IsSettled);
        Assert.False((await SettlementAsync(evt.Id, ledger.OwnerRep.Id))!.IsSettled);
    }

    // ============================ 6. Closed event parity (OQ-H) ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_ClosedEvent_CascadeFiresIdentically()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Chốt", Day14, Day16, closed: true);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status); // no EventWriteGuard rejection
        var binhShare = await ShareOfAsync(expense.Id, binh.Id);
        Assert.True(binhShare!.IsSettled); // cascade fires identically on a closed event
    }

    // ============================ 7. Soft-deleted target (§4.7) ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_SoftDeletedDebtor_CascadeStillFires()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình", deleted: true); // soft-deleted but still owing
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status);
        var binhShare = await ShareOfAsync(expense.Id, binh.Id);
        Assert.True(binhShare!.IsSettled); // history-preserving: a soft-deleted participant is still a valid cascade target
    }

    // ============================ 8. No audit row (OQ-G) ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_Cascade_WritesNoAuditRow()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));

        await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        await using var context = CreateContext();
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.ActorUserId == ledger.User.Id));
    }

    // ============================ 9. Cross-user isolation ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_NeverTouchesAnotherUsersShares()
    {
        var owner = await SeedLedgerAsync();
        var ownerBinh = await SeedMemberAsync(owner.User.Id, "Bình");
        var ownerEvt = await SeedEventAsync(owner.User.Id, "Của tôi", Day14, Day16);
        var ownerExpense = await SeedEventExpenseAsync(owner.User.Id, owner.OwnerRep.Id, owner.DefaultCategory.Id, ownerEvt.Id,
            (owner.OwnerRep.Id, 0m), (ownerBinh.Id, 500_000m));

        // A separate user with a coincidentally-identical shape (same amounts) - must never be touched.
        var other = await SeedLedgerAsync();
        var otherBinh = await SeedMemberAsync(other.User.Id, "Bình");
        var otherEvt = await SeedEventAsync(other.User.Id, "Của người khác", Day14, Day16);
        var otherExpense = await SeedEventExpenseAsync(other.User.Id, other.OwnerRep.Id, other.DefaultCategory.Id, otherEvt.Id,
            (other.OwnerRep.Id, 0m), (otherBinh.Id, 500_000m));

        await CreateSettlementRepository().SetMemberSettledAsync(owner.User.Uuid, ownerEvt.Uuid, ownerBinh.Uuid, isSettled: true);

        var ownerShare = await ShareOfAsync(ownerExpense.Id, ownerBinh.Id);
        Assert.True(ownerShare!.IsSettled);
        var otherShare = await ShareOfAsync(otherExpense.Id, otherBinh.Id);
        Assert.False(otherShare!.IsSettled); // never touched
        Assert.Null(await SettlementAsync(otherEvt.Id, otherBinh.Id)); // no settlement row created for the other user either
    }
}
