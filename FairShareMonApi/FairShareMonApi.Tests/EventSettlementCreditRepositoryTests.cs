using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for event-expense-settlement-sync Milestone 2 (Direction 2: expense/share settle
/// -&gt; partial credit to the event-level <c>EventMemberSettlement.ClearedAmount</c>, via the one shared
/// <see cref="EventSettlementCreditApplier"/> code path) against the real MariaDB (skippable). Per the
/// planning doc's Step M2.6 integration test list: whole-expense settle credits every eligible debtor
/// simultaneously while a creditor/mixed member on the same expense gets zero credit; a lone per-share
/// settle credits identically to the equivalent whole-expense path (the "one shared code path"
/// cross-trigger consistency check); idempotency (no double-credit/double-claw); the OQ-L cumulative
/// "corollary" fixture (reaching NetOwed auto-settles; a further non-billable debtor-share settle still
/// flips its own Layer A flag but contributes zero further credit); reversal floored at 0 and re-capped
/// at the member's CURRENT net owed (the open-event drift fixture); Direction 2 never touching a loose
/// expense; the D2/M7 OQ2 money-exactness invariant; no audit row; the migration regression (a
/// <c>ClearedAmount == 0</c> row computes the same Outstanding as the old boolean-only formula); and the
/// OQ2-confirmed cross-direction consequence (a manual Direction-1 full-settle followed by an unrelated
/// per-share Direction-2 reversal partially claws back the member's cleared amount).
/// </summary>
[Collection("AuthIntegration")]
public class EventSettlementCreditRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
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

    private async Task<EventMemberSettlement?> SettlementAsync(ulong eventId, ulong memberId)
    {
        await using var context = CreateContext();
        return await context.EventMemberSettlements.AsNoTracking()
            .SingleOrDefaultAsync(settlement => settlement.EventId == eventId && settlement.MemberId == memberId);
    }

    // ============================ 1. Whole-expense settle: every eligible debtor credited, creditor gets zero ============================

    [SkippableFact]
    public async Task SetSettledAsync_WholeExpense_CreditsEveryEligibleDebtor_CreditorOnSameExpenseGetsZero()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var dung = await SeedMemberAsync(ledger.User.Id, "Dũng");
        var en = await SeedMemberAsync(ledger.User.Id, "Én");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // Expense 1: An pays; Bình owes 300k, Cường owes 500k, Dũng owes 200k.
        var expense1 = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 300_000m), (cuong.Id, 500_000m), (dung.Id, 200_000m));
        // Expense 2: Dũng pays (Advanced = the WHOLE expense total, i.e. the sum of its shares); Én owes
        // 700k of it, Dũng's own share is 0đ -> Dũng's overall Advanced=700k, Owed=200k (from expense1) =>
        // Balance +500k (net creditor), NetOwed == 0 - DESPITE also holding a debtor-share on expense1.
        await SeedEventExpenseAsync(ledger.User.Id, dung.Id, ledger.DefaultCategory.Id, evt.Id,
            (dung.Id, 0m), (en.Id, 700_000m));

        var status = await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense1.Uuid, true);
        Assert.Equal(ExpenseWriteStatus.Success, status);

        // Bình: NetOwed = 300k exactly (only debt), fully credited, capped at exactly her own net owed.
        var binhSettlement = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(300_000m, binhSettlement!.ClearedAmount);
        Assert.True(binhSettlement.IsSettled);

        // Cường: NetOwed = 500k exactly (only debt), fully credited by this one settle.
        var cuongSettlement = await SettlementAsync(evt.Id, cuong.Id);
        Assert.Equal(500_000m, cuongSettlement!.ClearedAmount);
        Assert.True(cuongSettlement.IsSettled);
        Assert.NotNull(cuongSettlement.SettledAt);

        // Dũng: net creditor overall (NetOwed == 0) -> self-protecting clamp yields ZERO credit despite
        // holding a billable, now-settled debtor-share on the very expense that was just toggled.
        var dungSettlement = await SettlementAsync(evt.Id, dung.Id);
        Assert.NotNull(dungSettlement); // row IS created (ApplyAsync always upserts for every affected member)
        Assert.Equal(0m, dungSettlement!.ClearedAmount);
        Assert.False(dungSettlement.IsSettled);

        // But the Layer A per-share flag still flips true for Dũng's debtor share (unconditional per OQ6a).
        var dungShare1 = await ShareOfAsync(expense1.Id, dung.Id);
        Assert.True(dungShare1!.IsSettled);
    }

    // ============================ 2. Cross-trigger consistency: per-share == whole-expense for an equivalent scenario ============================

    [SkippableFact]
    public async Task SetSettledAsync_LonePerShareSettle_CreditsIdenticallyToEquivalentWholeExpenseSettle()
    {
        var ledger = await SeedLedgerAsync();

        // Scenario A: whole-expense settle.
        var binhA = await SeedMemberAsync(ledger.User.Id, "Bình A");
        var evtA = await SeedEventAsync(ledger.User.Id, "Đợt A", Day14, Day16);
        var expenseA = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evtA.Id,
            (ledger.OwnerRep.Id, 0m), (binhA.Id, 500_000m));
        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expenseA.Uuid, true);
        var settlementA = await SettlementAsync(evtA.Id, binhA.Id);

        // Scenario B: lone per-share settle on an equivalent single-share expense (same amount).
        var binhB = await SeedMemberAsync(ledger.User.Id, "Bình B");
        var evtB = await SeedEventAsync(ledger.User.Id, "Đợt B", Day14, Day16);
        var expenseB = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evtB.Id,
            (ledger.OwnerRep.Id, 0m), (binhB.Id, 500_000m));
        var shareB = await ShareOfAsync(expenseB.Id, binhB.Id);
        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expenseB.Uuid, shareB!.Uuid, true);
        var settlementB = await SettlementAsync(evtB.Id, binhB.Id);

        Assert.Equal(settlementA!.ClearedAmount, settlementB!.ClearedAmount);
        Assert.Equal(settlementA.IsSettled, settlementB.IsSettled);
        Assert.Equal(500_000m, settlementB.ClearedAmount);
        Assert.True(settlementB.IsSettled);
    }

    // ============================ 3. Idempotency ============================

    [SkippableFact]
    public async Task SetSettledAsync_ReSettleAlreadySettledShare_DoesNotDoubleCredit()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));
        var share = await ShareOfAsync(expense.Id, binh.Id);
        var shareRepo = CreateShareRepository();

        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share!.Uuid, true);
        Assert.Equal(500_000m, (await SettlementAsync(evt.Id, binh.Id))!.ClearedAmount);

        // Re-settle an already-settled share: wasSettled == isSettled -> structurally no delta.
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share.Uuid, true);

        Assert.Equal(500_000m, (await SettlementAsync(evt.Id, binh.Id))!.ClearedAmount); // NOT doubled to 1,000,000
    }

    [SkippableFact]
    public async Task SetSettledAsync_ReUnsettleAlreadyUnsettledShare_DoesNotDoubleClaw()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));
        var share = await ShareOfAsync(expense.Id, binh.Id);
        var shareRepo = CreateShareRepository();

        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share!.Uuid, true);
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share.Uuid, false);
        Assert.Equal(0m, (await SettlementAsync(evt.Id, binh.Id))!.ClearedAmount);

        // Re-unsettle an already-unsettled share: wasSettled == isSettled (both false) -> no delta.
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share.Uuid, false);

        Assert.Equal(0m, (await SettlementAsync(evt.Id, binh.Id))!.ClearedAmount); // stays floored at 0, not clawed further
    }

    // ============================ 4. OQ-L "corollary": cumulative credit -> auto-settle; further non-billable settle contributes zero ============================

    [SkippableFact]
    public async Task SetSettledAsync_CumulativeCreditReachesNetOwed_AutoSettles_FurtherZeroShareSettleFlipsFlagButAddsNoCredit()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense1 = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 300_000m));
        var expense2 = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 200_000m));
        var share1 = await ShareOfAsync(expense1.Id, binh.Id);
        var share2 = await ShareOfAsync(expense2.Id, binh.Id);
        var shareRepo = CreateShareRepository();

        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense1.Uuid, share1!.Uuid, true); // +300k
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense2.Uuid, share2!.Uuid, true); // +200k -> 500k == NetOwed

        var afterFull = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(500_000m, afterFull!.ClearedAmount);
        Assert.True(afterFull.IsSettled);
        Assert.NotNull(afterFull.SettledAt);

        // A THIRD expense where Bình holds a 0đ share (a "debtor share" in name only - non-billable,
        // Amount == 0). Settling it flips the share's own Layer A flag unconditionally (OQ6a/OQ-D) but
        // never reaches EventSettlementCreditApplier (SettlementReconciler.IsBillable requires Amount > 0),
        // so it contributes ZERO further credit - the OQ-L "corollary" (more settled per-share badges than
        // the capped event-level credit implies).
        var expense3 = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 0m));
        var share3 = await ShareOfAsync(expense3.Id, binh.Id);
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense3.Uuid, share3!.Uuid, true);

        var share3Reloaded = await ShareOfAsync(expense3.Id, binh.Id);
        Assert.True(share3Reloaded!.IsSettled); // Layer A flag still flips

        var afterThird = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(500_000m, afterThird!.ClearedAmount); // unchanged - zero further credit
        Assert.True(afterThird.IsSettled);
    }

    // ============================ 5. Reversal: floored at 0 ============================

    [SkippableFact]
    public async Task SetSettledAsync_UnsettleContributingShare_ClawsBackExactAmount_FlooredAtZero()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));
        var share = await ShareOfAsync(expense.Id, binh.Id);
        var shareRepo = CreateShareRepository();

        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share!.Uuid, true);
        Assert.Equal(500_000m, (await SettlementAsync(evt.Id, binh.Id))!.ClearedAmount);

        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share.Uuid, false);

        var settlement = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(0m, settlement!.ClearedAmount); // clawed back exactly, floored at 0
        Assert.False(settlement.IsSettled);
        Assert.Null(settlement.SettledAt);
    }

    // ============================ 6. Reversal: re-capped at CURRENT net owed (open-event drift) ============================

    [SkippableFact]
    public async Task SetSettledAsync_UnsettleAfterShareAmountReduced_RecapsClawbackAtCurrentNetOwed()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));
        var share = await ShareOfAsync(expense.Id, binh.Id);
        var shareRepo = CreateShareRepository();

        // Fully credit at the ORIGINAL amount (500k == NetOwed at the time).
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share!.Uuid, true);
        Assert.Equal(500_000m, (await SettlementAsync(evt.Id, binh.Id))!.ClearedAmount);

        // The event's shares change AFTER the credit was applied: Bình's debt is reduced to 100k
        // (an open-event edit, §4.4 sole exception aside - this expense's event is still OPEN here).
        var updateResult = await shareRepo.UpdateAsync(
            ledger.User.Uuid, expense.Uuid, share.Uuid, new ShareData(binh.Uuid, 100_000m, null));
        Assert.Equal(ExpenseWriteStatus.Success, updateResult.Status);

        // Unsettle: the delta uses the CURRENT share amount (100k, not the original 500k credited), and
        // the clamp's upper bound is the CURRENT NetOwed (100k) - naive subtraction (500 - 100 = 400)
        // would overshoot; the applier re-caps down to the member's current net owed instead.
        await shareRepo.SetSettledAsync(ledger.User.Uuid, expense.Uuid, share.Uuid, false);

        var settlement = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(100_000m, settlement!.ClearedAmount); // re-capped at current NetOwed, not 400k
        // Per the shared clamp's own semantics (Decision Log entry 5): newCleared (100k) still equals the
        // current NetOwed (100k) exactly, so the member reads as fully settled at their NEW, smaller debt -
        // an accepted consequence of "recomputed against current data, no provenance tracking" (OQ1/OQ-C).
        Assert.True(settlement.IsSettled);
    }

    // ============================ 7. Loose expense: Direction 2 never applies ============================

    [SkippableFact]
    public async Task SetSettledAsync_LooseExpense_NeverCreatesOrTouchesEventMemberSettlement()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            new CreateExpenseData("Ăn trưa", null, Noon, null, null, [], [new CreateShareData(binh.Uuid, 500_000m, null)]));
        Assert.Equal(ExpenseWriteStatus.Success, created.Status);
        var expense = created.Entity!;
        Assert.Null(expense.EventId); // loose, per Assumptions section
        var share = expense.Shares.Single(s => s.MemberId == binh.Id);

        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, share.Uuid, true);

        await using var context = CreateContext();
        Assert.Equal(0, await context.EventMemberSettlements.CountAsync(s => s.MemberId == binh.Id));
    }

    [SkippableFact]
    public async Task SetSettledAsync_LooseExpenseWholeToggle_NeverCreatesOrTouchesEventMemberSettlement()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            new CreateExpenseData("Ăn trưa", null, Noon, null, null, [], [new CreateShareData(binh.Uuid, 500_000m, null)]));
        var expense = created.Entity!;

        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, true);

        await using var context = CreateContext();
        Assert.Equal(0, await context.EventMemberSettlements.CountAsync(s => s.MemberId == binh.Id));
    }

    // ============================ 8. Money-exactness regression (D2/M7 OQ2) ============================

    [SkippableFact]
    public async Task SetSettledAsync_DirectionTwoWrite_LeavesAdvancedOwedBalanceByteForByteUnchanged()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 300_000m), (cuong.Id, 200_000m));

        var before = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, true);

        var after = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        foreach (var row in before)
        {
            var match = Assert.Single(after, r => r.MemberUuid == row.MemberUuid);
            Assert.Equal(row.Advanced, match.Advanced);
            Assert.Equal(row.Owed, match.Owed);
            Assert.Equal(row.Advanced - row.Owed, match.Advanced - match.Owed); // balance
        }
        Assert.Equal(0m, after.Sum(row => row.Advanced - row.Owed)); // sum-to-zero preserved
    }

    // ============================ 9. No audit row ============================

    [SkippableFact]
    public async Task SetSettledAsync_CreditStep_WritesNoAuditRow()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expense = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));
        var share = await ShareOfAsync(expense.Id, binh.Id);

        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, share!.Uuid, true);
        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, false); // exercise the whole-expense path too

        await using var context = CreateContext();
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.ActorUserId == ledger.User.Id));
    }

    // ============================ 10. Migration regression ============================

    [SkippableFact]
    public async Task GetEventBalanceAsync_UnsettledLegacyRowWithZeroClearedAmount_ComputesSameOutstandingAsOldBooleanFormula()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m));

        // Simulate a pre-migration row: IsSettled = false, ClearedAmount left at its column default (0) -
        // never touched by EventSettlementCreditApplier or the manual Layer B path.
        await using (var context = CreateContext())
        {
            context.EventMemberSettlements.Add(new EventMemberSettlement { EventId = evt.Id, MemberId = binh.Id, IsSettled = false });
            await context.SaveChangesAsync();
        }

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        var binhRow = Assert.Single(rows, row => row.MemberUuid == binh.Uuid);

        Assert.Equal(0m, binhRow.ClearedAmount); // legacy default
        var netOwed = binhRow.Advanced - binhRow.Owed < 0m ? -(binhRow.Advanced - binhRow.Owed) : 0m;
        var newFormulaOutstanding = Math.Max(0m, netOwed - binhRow.ClearedAmount);
        var oldBooleanFormulaOutstanding = binhRow.IsSettled ? 0m : netOwed; // the pre-M2 formula
        Assert.Equal(oldBooleanFormulaOutstanding, newFormulaOutstanding);
        Assert.Equal(500_000m, newFormulaOutstanding);
    }

    // ============================ 11. OQ2 cross-direction consequence ============================

    [SkippableFact]
    public async Task SetMemberSettledAsync_ManualFullSettle_ThenUnrelatedPerShareReversal_PartiallyClawsBackClearedAmount()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        var expenseX = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 300_000m));
        var expenseY = await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 200_000m));

        // Manual Direction-1 full-settle at the event level (Bình is a net debtor -> eligible, so this
        // ALSO cascades both shares' Layer A flags via M1.2, and snapshots ClearedAmount = NetOwed via M2.4).
        var settleStatus = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);
        Assert.Equal(SettlementWriteStatus.Success, settleStatus);
        var afterManualSettle = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(500_000m, afterManualSettle!.ClearedAmount);
        Assert.True(afterManualSettle.IsSettled);

        // An UNRELATED per-share Direction-2 reversal - un-settling just expenseX's share via
        // ShareRepository, NOT via the event-level SetMemberSettledAsync(false) path.
        var shareX = await ShareOfAsync(expenseX.Id, binh.Id);
        await CreateShareRepository().SetSettledAsync(ledger.User.Uuid, expenseX.Uuid, shareX!.Uuid, false);

        var afterReversal = await SettlementAsync(evt.Id, binh.Id);
        Assert.Equal(200_000m, afterReversal!.ClearedAmount); // 500k - 300k clawed back = 200k (partial)
        Assert.False(afterReversal.IsSettled); // no longer fully settled - the accepted OQ2 consequence

        // expenseY's share is untouched by this reversal (only expenseX's was targeted).
        var shareY = await ShareOfAsync(expenseY.Id, binh.Id);
        Assert.True(shareY!.IsSettled);
    }
}
