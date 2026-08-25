using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Four-way classification of a member's net-balance role in one event (event-expense-settlement-sync
/// Step M1.1): whether they are a net debtor, a net creditor, or net-zero, and - for a net creditor -
/// whether they hold NO debtor-share anywhere else in the event ("gross-pure") or at least one
/// ("gross-mixed"). Direction 1's auto-cascade eligibility gate is a net debtor OR a gross-pure net
/// creditor (a gross-mixed creditor falls back to manual per-share toggling, OQ-A/OQ-L).
/// </summary>
public enum MemberSettlementEligibility
{
    /// <summary>Advanced == owed exactly - the "mixed"/ineligible residual bucket (assumption, see planning doc).</summary>
    NetZero,

    /// <summary>Advanced &lt; owed - always eligible for Direction 1's auto-cascade.</summary>
    NetDebtor,

    /// <summary>Advanced &gt; owed and holds no debtor-share anywhere in the event - eligible for Direction 1's auto-cascade.</summary>
    NetCreditorGrossPure,

    /// <summary>Advanced &gt; owed but holds at least one debtor-share elsewhere in the event - NOT eligible; falls back to manual toggling.</summary>
    NetCreditorGrossMixed
}

/// <summary>
/// One member's advanced/owed figures and gross-purity fact for one event, plus the derived
/// classification. <see cref="Balance"/>/<see cref="NetOwed"/> are computed, not stored.
/// </summary>
public sealed record MemberSettlementFacts(
    ulong MemberId,
    decimal Advanced,
    decimal Owed,
    bool HasDebtorShareElsewhereInEvent,
    MemberSettlementEligibility Eligibility)
{
    /// <summary>Advanced - owed. Negative = the member owes; positive = the member is owed.</summary>
    public decimal Balance => Advanced - Owed;

    /// <summary>The member's net owed amount (0 when they are not a net debtor).</summary>
    public decimal NetOwed => Balance < 0m ? -Balance : 0m;

    /// <summary>True only for <see cref="MemberSettlementEligibility.NetDebtor"/> or <see cref="MemberSettlementEligibility.NetCreditorGrossPure"/>.</summary>
    public bool IsEligibleForDirection1Cascade =>
        Eligibility is MemberSettlementEligibility.NetDebtor or MemberSettlementEligibility.NetCreditorGrossPure;
}

/// <summary>
/// The ONE canonical place the single-sided/gross-purity eligibility logic lives (event-expense-settlement-sync
/// planning doc's Risks section calls a second, divergent implementation the single biggest risk in this
/// feature). Split into a pure, DB-free classification function (unit-testable in isolation) and a
/// DB-querying half reused by both <c>StatsRepository</c> (read path) and every Direction 1/2 write path.
/// A plain static class taking <see cref="AppDbContext"/> directly - safe to call from any repository's
/// own transaction because <c>AppDbContext</c> is Scoped (<c>AddDbContextPool</c> in <c>Program.cs</c>),
/// so every repository resolved in one request/DI-scope shares the same instance.
/// </summary>
public static class EventSettlementClassifier
{
    /// <summary>Pure classification: the switch on <c>advanced - owed</c> plus the gross-purity fact. No DB access.</summary>
    public static MemberSettlementEligibility Classify(decimal advanced, decimal owed, bool hasDebtorShareElsewhere)
    {
        var balance = advanced - owed;
        if (balance == 0m)
            return MemberSettlementEligibility.NetZero;

        if (balance < 0m)
            return MemberSettlementEligibility.NetDebtor;

        return hasDebtorShareElsewhere
            ? MemberSettlementEligibility.NetCreditorGrossMixed
            : MemberSettlementEligibility.NetCreditorGrossPure;
    }

    /// <summary>
    /// Computes <see cref="MemberSettlementFacts"/> for every participant of an event (or, when
    /// <paramref name="restrictToMemberIds"/> is non-empty, only the given members - Direction 1's
    /// single-member eligibility check; Direction 2's per-expense debtor set), always aggregated over the
    /// FULL event share-set so <c>Σ balance == 0</c> is preserved regardless of the restriction. Runs the
    /// exact same advanced/owed <c>GroupBy</c>/<c>Sum</c> shape as <c>StatsRepository.GetEventBalanceAsync</c>
    /// (which consumes this same helper, closing the gross/net duplication-drift risk), plus one more
    /// query for the gross-purity fact (a billable/debtor share - <c>Amount &gt; 0 &amp;&amp; MemberId !=
    /// Expense.PayerMemberId</c>, the <see cref="SettlementReconciler.IsBillable"/>-equivalent predicate -
    /// held anywhere in the event).
    /// </summary>
    public static async Task<IReadOnlyDictionary<ulong, MemberSettlementFacts>> ClassifyAsync(
        AppDbContext dbContext,
        ulong eventId,
        IReadOnlyCollection<ulong>? restrictToMemberIds,
        CancellationToken cancellationToken)
    {
        var shares = dbContext.Set<Share>().AsNoTracking()
            .Where(share => share.Expense.EventId == eventId);

        // Advanced per payer (DB-side SUM grouped by the expense's payer) - identical shape to StatsRepository.
        var advancedByPayer = await shares
            .GroupBy(share => share.Expense.PayerMemberId)
            .Select(group => new { MemberId = group.Key, Amount = group.Sum(share => share.Amount) })
            .ToListAsync(cancellationToken);

        // Owed per member (DB-side SUM grouped by the share's member) - SAME share-set.
        var owedByMember = await shares
            .GroupBy(share => share.MemberId)
            .Select(group => new { MemberId = group.Key, Amount = group.Sum(share => share.Amount) })
            .ToListAsync(cancellationToken);

        var advancedMap = advancedByPayer.ToDictionary(row => row.MemberId, row => row.Amount);
        var owedMap = owedByMember.ToDictionary(row => row.MemberId, row => row.Amount);

        IEnumerable<ulong> memberIds = advancedMap.Keys.Union(owedMap.Keys);
        if (restrictToMemberIds is { Count: > 0 })
            memberIds = memberIds.Intersect(restrictToMemberIds);
        var memberIdList = memberIds.ToList();

        if (memberIdList.Count == 0)
            return new Dictionary<ulong, MemberSettlementFacts>();

        // Gross-purity fact: does the member hold a debtor-share (billable, per SettlementReconciler.IsBillable)
        // ANYWHERE in the event? Computed over the same full share-set regardless of the restriction.
        var debtorShareMemberIds = await shares
            .Where(share => share.Amount > 0m && share.MemberId != share.Expense.PayerMemberId)
            .Select(share => share.MemberId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var debtorShareMemberIdSet = debtorShareMemberIds.ToHashSet();

        return memberIdList.ToDictionary(
            memberId => memberId,
            memberId =>
            {
                var advanced = advancedMap.GetValueOrDefault(memberId, 0m);
                var owed = owedMap.GetValueOrDefault(memberId, 0m);
                var hasDebtorShareElsewhere = debtorShareMemberIdSet.Contains(memberId);
                var eligibility = Classify(advanced, owed, hasDebtorShareElsewhere);
                return new MemberSettlementFacts(memberId, advanced, owed, hasDebtorShareElsewhere, eligibility);
            });
    }
}
