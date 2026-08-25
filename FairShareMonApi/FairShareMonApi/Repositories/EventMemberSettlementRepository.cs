using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Outcome of a per-member-per-event net-clearance write (Layer B). Carries the resource-owned miss
/// variants back to the service (mapped there to the 9xxx/3xxx <c>ErrorException</c>s) instead of
/// throwing across the transaction boundary (mirrors <see cref="EventWriteStatus"/>).
/// </summary>
public enum SettlementWriteStatus
{
    /// <summary>The write succeeded.</summary>
    Success,

    /// <summary>The event (or owning user) was not found within the caller's scope (9000).</summary>
    EventNotFound,

    /// <summary>The member is foreign/unknown or does not participate in the event (3000, settled-per-member OQ9a/OQ12a).</summary>
    MemberNotFound
}

/// <summary>
/// Data access for the per-member-per-event net-clearance flag (Layer B of settled-per-member, §3.7/§6,
/// table <c>event_member_settlements</c>). The write is resource-owned: scoped by the owning user's UUID
/// so another user's events/members never leak (a miss yields <c>EventNotFound</c>/<c>MemberNotFound</c>,
/// never the row). Runs in a single <c>ExecuteTransactionAsync</c>. There is <b>no closed-event guard</b>
/// (the §4.4 sole exception - Layer B is primarily a post-close action, settled-per-member OQ5a) and
/// <b>no audit</b> (OQ10a). The balance overlay is read separately by <c>StatsRepository</c> (kept pure).
///
/// <para>
/// event-expense-settlement-sync Milestone 1 (Direction 1): setting the flag to <c>true</c> automatically
/// cascades to ALL of the member's <see cref="Share"/> rows in the event - gated by
/// <c>EventSettlementClassifier</c>'s eligibility check (a net debtor is always eligible; a net creditor
/// only if gross-pure, i.e. holds no debtor-share anywhere else in the event; a "mixed"/net-zero member
/// never cascades and must be settled per-share manually, OQ-A/OQ-L). Setting it back to <c>false</c>
/// unconditionally reverses the same "all shares in the event" set, recomputed against CURRENT data,
/// regardless of the member's eligibility today (OQ1, option (a)). Bypasses <c>EventWriteGuard</c> the
/// same way the flag write itself already does - no new bypass code needed. Not audited.
/// </para>
/// </summary>
public interface IEventMemberSettlementRepository : IBaseRepository
{
    /// <summary>
    /// Upserts the <c>(event_id, member_id)</c> settlement flag. Resolves + owns the event (miss -&gt;
    /// <see cref="SettlementWriteStatus.EventNotFound"/>) and resolves the member as an owned participant
    /// of the event - a payer of, or share-holder in, one of its expenses (else
    /// <see cref="SettlementWriteStatus.MemberNotFound"/>, settled-per-member OQ9a). Allowed on OPEN and
    /// CLOSED events (OQ5a). Soft-deleted participants are still markable (§4.7).
    ///
    /// <para>
    /// event-expense-settlement-sync Direction 1: on <paramref name="isSettled"/> <c>true</c>, if the
    /// member is eligible (net debtor, or a gross-pure net creditor), also settles every one of the
    /// member's shares across all of the event's expenses and reconciles each affected expense's
    /// whole-flag; an ineligible member's flag still flips, but no share is touched. On <c>false</c>, the
    /// same "all shares in the event" set is unconditionally un-settled, recomputed live against current
    /// data (OQ1).
    /// </para>
    /// </summary>
    Task<SettlementWriteStatus> SetMemberSettledAsync(string userUuid, string eventUuid, string memberUuid, bool isSettled, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IEventMemberSettlementRepository))]
public sealed class EventMemberSettlementRepository(AppDbContext dbContext)
    : BaseRepository(dbContext), IEventMemberSettlementRepository
{
    public Task<SettlementWriteStatus> SetMemberSettledAsync(string userUuid, string eventUuid, string memberUuid, bool isSettled, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            // Resource-owned event (miss -> EventNotFound). No closed-event guard (§4.4 exception, OQ5a).
            var evt = await Query<Event>()
                .FirstOrDefaultAsync(entity => entity.Uuid == eventUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (evt is null)
            {
                transaction.NoCommit();
                return SettlementWriteStatus.EventNotFound;
            }

            // Resolve the member owned by the caller (incl. soft-deleted, §4.7); a foreign/unknown member is a miss.
            var member = await Query<Member>(includeDeleted: true)
                .FirstOrDefaultAsync(entity => entity.Uuid == memberUuid && entity.User.Uuid == userUuid, cancellationToken);
            if (member is null)
            {
                transaction.NoCommit();
                return SettlementWriteStatus.MemberNotFound;
            }

            // Participant only: a payer of, or share-holder in, one of the event's expenses (OQ9a).
            var participates = await Query<Expense>()
                .Where(expense => expense.EventId == evt.Id && expense.User.Uuid == userUuid)
                .AnyAsync(expense => expense.PayerMemberId == member.Id
                    || expense.Shares.Any(share => share.MemberId == member.Id), cancellationToken);
            if (!participates)
            {
                transaction.NoCommit();
                return SettlementWriteStatus.MemberNotFound;
            }

            // Upsert the (event, member) flag - always succeeds for any participant, eligible or not.
            var settlement = await Query<EventMemberSettlement>(tracking: true)
                .FirstOrDefaultAsync(entity => entity.EventId == evt.Id && entity.MemberId == member.Id, cancellationToken);
            if (settlement is null)
            {
                settlement = new EventMemberSettlement { EventId = evt.Id, MemberId = member.Id };
                db.EventMemberSettlements.Add(settlement);
            }

            settlement.IsSettled = isSettled;
            settlement.SettledAt = isSettled ? AppDateTime.Now : null;
            // No audit (OQ10a).

            // Direction 1 (event-expense-settlement-sync M1.2): classify against CURRENT data.
            var facts = await EventSettlementClassifier.ClassifyAsync(db, evt.Id, [member.Id], cancellationToken);
            if (!facts.TryGetValue(member.Id, out var memberFacts))
                memberFacts = new MemberSettlementFacts(member.Id, 0m, 0m, false, MemberSettlementEligibility.NetZero);

            if (isSettled)
            {
                // Eligible (net debtor, or gross-pure net creditor) -> cascade to ALL of the member's
                // shares in the event (OQ-B); ineligible -> no share write at all, silent fallback to
                // manual per-share toggling (OQ-A/OQ-L).
                if (memberFacts.IsEligibleForDirection1Cascade)
                {
                    var expenses = await LoadMemberExpensesAsync(evt.Id, member.Id, cancellationToken);
                    CascadeMemberShares(expenses, member.Id, isSettled: true, settledAt: AppDateTime.Now);
                }
            }
            else
            {
                // OQ1, option (a): unconditional, recomputed live - reverse the same "all shares in the
                // event" set regardless of whether the member is still eligible today.
                var expenses = await LoadMemberExpensesAsync(evt.Id, member.Id, cancellationToken);
                CascadeMemberShares(expenses, member.Id, isSettled: false, settledAt: null);
            }

            return SettlementWriteStatus.Success;
        }, cancellationToken);

    /// <summary>Loads every event expense where the member holds a share, tracked with its shares (for the Direction 1 cascade/reversal).</summary>
    private Task<List<Expense>> LoadMemberExpensesAsync(ulong eventId, ulong memberId, CancellationToken cancellationToken) =>
        Query<Expense>(tracking: true)
            .Where(expense => expense.EventId == eventId && expense.Shares.Any(share => share.MemberId == memberId))
            .Include(expense => expense.Shares)
            .ToListAsync(cancellationToken);

    /// <summary>Sets the member's own share(s) settled flag on each loaded expense and reconciles the expense's whole-flag.</summary>
    private static void CascadeMemberShares(IEnumerable<Expense> expenses, ulong memberId, bool isSettled, DateTime? settledAt)
    {
        foreach (var expense in expenses)
        {
            foreach (var share in expense.Shares.Where(share => share.MemberId == memberId))
            {
                share.IsSettled = isSettled;
                share.SettledAt = settledAt;
            }

            SettlementReconciler.ReconcileExpense(expense);
        }
    }
}
