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
/// </summary>
public interface IEventMemberSettlementRepository : IBaseRepository
{
    /// <summary>
    /// Upserts the <c>(event_id, member_id)</c> settlement flag. Resolves + owns the event (miss -&gt;
    /// <see cref="SettlementWriteStatus.EventNotFound"/>) and resolves the member as an owned participant
    /// of the event - a payer of, or share-holder in, one of its expenses (else
    /// <see cref="SettlementWriteStatus.MemberNotFound"/>, settled-per-member OQ9a). Allowed on OPEN and
    /// CLOSED events (OQ5a). Soft-deleted participants are still markable (§4.7).
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

            // Upsert the (event, member) flag.
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
            return SettlementWriteStatus.Success;
        }, cancellationToken);
}
