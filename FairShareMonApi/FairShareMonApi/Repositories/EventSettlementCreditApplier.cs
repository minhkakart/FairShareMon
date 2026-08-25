using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// The ONE shared code path Direction 2 (event-expense-settlement-sync Milestone 2) funnels through -
/// both <c>ShareRepository.SetSettledAsync</c> and <c>ExpenseRepository.SetSettledAsync</c> call this
/// same static method (OQ-D residual, Decision Log entry 6 in the BA doc / this doc's own Decision Log).
/// Mirrors <see cref="SettlementReconciler"/>'s shape: a plain static class taking <see cref="AppDbContext"/>
/// directly, safe to call from another repository's own transaction because <c>AppDbContext</c> is Scoped.
/// </summary>
public static class EventSettlementCreditApplier
{
    /// <summary>
    /// Applies a batch of per-member credit/claw-back deltas to their <see cref="EventMemberSettlement"/>
    /// rows for one event. <paramref name="deltas"/>'s <c>Delta</c> is <c>+share.Amount</c> on a settle,
    /// <c>-share.Amount</c> on an un-settle. For each affected member: clamps
    /// <c>newCleared = Clamp(existing.ClearedAmount + delta, 0, facts.NetOwed)</c> - a creditor or
    /// <c>NetZero</c> member has <c>NetOwed == 0</c>, so the clamp collapses to 0 with no separate
    /// eligibility branch needed (self-protecting, Decision Log entry 5's own finding). Flips
    /// <see cref="EventMemberSettlement.IsSettled"/>/<see cref="EventMemberSettlement.SettledAt"/> when
    /// crossing the full/partial boundary (<c>NetOwed &gt; 0 &amp;&amp; newCleared &gt;= NetOwed</c>). Creates
    /// a settlement row on demand for a member with no prior row. No audit.
    /// </summary>
    public static async Task ApplyAsync(
        AppDbContext db,
        ulong eventId,
        IReadOnlyList<(ulong MemberId, decimal Delta)> deltas,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (deltas.Count == 0)
            return;

        var memberIds = deltas.Select(delta => delta.MemberId).Distinct().ToList();

        var facts = await EventSettlementClassifier.ClassifyAsync(db, eventId, memberIds, cancellationToken);

        var existingSettlements = await db.EventMemberSettlements
            .Where(settlement => settlement.EventId == eventId && memberIds.Contains(settlement.MemberId))
            .ToDictionaryAsync(settlement => settlement.MemberId, cancellationToken);

        foreach (var (memberId, delta) in deltas)
        {
            if (!facts.TryGetValue(memberId, out var memberFacts))
                memberFacts = new MemberSettlementFacts(memberId, 0m, 0m, false, MemberSettlementEligibility.NetZero);

            if (!existingSettlements.TryGetValue(memberId, out var settlement))
            {
                settlement = new EventMemberSettlement { EventId = eventId, MemberId = memberId };
                db.EventMemberSettlements.Add(settlement);
                existingSettlements[memberId] = settlement;
            }

            var newCleared = Math.Clamp(settlement.ClearedAmount + delta, 0m, memberFacts.NetOwed);
            settlement.ClearedAmount = newCleared;

            var fullySettled = memberFacts.NetOwed > 0m && newCleared >= memberFacts.NetOwed;
            if (fullySettled != settlement.IsSettled)
            {
                settlement.IsSettled = fullySettled;
                settlement.SettledAt = fullySettled ? now : null;
            }
        }
    }
}
