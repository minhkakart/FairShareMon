using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="EventShareLink"/> rows (planning/event-share-link.md). Owner-scoped
/// reads/writes are resource-owned (an ownership miss yields null, never the row); token lookup is
/// deliberately <b>anonymous</b> (not user-scoped) - the anonymous public read resolves owner + event
/// from the token, and the service decides validity (expired/revoked/unknown all map to 16000). Links
/// are soft-revoked (<see cref="EventShareLink.RevokedAt"/>), never hard-deleted, so a natural-expiry
/// purge could reclaim rows later.
/// </summary>
public interface IEventShareLinkRepository : IBaseRepository
{
    /// <summary>Inserts a new share link for the owner's event (resolves owner + event scoped by <paramref name="userUuid"/>; a defensive ownership miss -&gt; <c>EventNotFound</c> 9000). The caller has already validated ownership.</summary>
    Task<EventShareLink> CreateAsync(
        string userUuid,
        string eventUuid,
        string token,
        DateTime expiresAt,
        string? bankAccountUuid,
        string? bankBin,
        string? bankName,
        string? accountNumber,
        string? accountHolderName,
        CancellationToken cancellationToken = default);

    /// <summary>The event's current active (not revoked, not expired) link scoped to the owner; null when none.</summary>
    Task<EventShareLink?> GetActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>Soft-revokes the event's active link (owner-scoped). Returns whether one was revoked and its token (for cache eviction).</summary>
    Task<(bool Revoked, string? Token)> RevokeActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>Anonymous token lookup (NOT user-scoped), including the owner + event. Returns the row regardless of expiry/revoke; the service decides validity. Null when the token is unknown.</summary>
    Task<EventShareLink?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IEventShareLinkRepository))]
public sealed class EventShareLinkRepository(AppDbContext dbContext) : BaseRepository(dbContext), IEventShareLinkRepository
{
    public Task<EventShareLink> CreateAsync(
        string userUuid,
        string eventUuid,
        string token,
        DateTime expiresAt,
        string? bankAccountUuid,
        string? bankBin,
        string? bankName,
        string? accountNumber,
        string? accountHolderName,
        CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            // Resolve the owner's event (resource-owned). The service pre-validates ownership, so a miss
            // here is defensive: abort the write and surface the standard event-not-found (9000).
            var owned = await db.Events.AsNoTracking()
                .Where(evt => evt.Uuid == eventUuid && evt.User.Uuid == userUuid)
                .Select(evt => new { evt.Id, evt.UserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (owned is null)
            {
                transaction.NoCommit();
                throw new ErrorException(ErrorCodes.EventNotFound, MessageKeys.Error.EventNotFound);
            }

            var link = new EventShareLink
            {
                UserId = owned.UserId,
                EventId = owned.Id,
                Token = token,
                ExpiresAt = expiresAt,
                BankAccountUuid = bankAccountUuid,
                BankBin = bankBin,
                BankName = bankName,
                AccountNumber = accountNumber,
                AccountHolderName = accountHolderName
            };
            db.EventShareLinks.Add(link);
            return link;
        }, cancellationToken);

    public Task<EventShareLink?> GetActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) =>
        {
            var now = AppDateTime.Now;
            return Query<EventShareLink>()
                .Where(link => link.User.Uuid == userUuid
                    && link.Event.Uuid == eventUuid
                    && link.RevokedAt == null
                    && link.ExpiresAt > now)
                .OrderByDescending(link => link.CreatedAt)
                .ThenByDescending(link => link.Id)
                .FirstOrDefaultAsync(ct);
        }, cancellationToken);

    public Task<(bool Revoked, string? Token)> RevokeActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var now = AppDateTime.Now;
            var link = await Query<EventShareLink>(tracking: true)
                .Where(existing => existing.User.Uuid == userUuid
                    && existing.Event.Uuid == eventUuid
                    && existing.RevokedAt == null
                    && existing.ExpiresAt > now)
                .OrderByDescending(existing => existing.CreatedAt)
                .ThenByDescending(existing => existing.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (link is null)
            {
                transaction.NoCommit();
                return (false, (string?)null);
            }

            link.RevokedAt = now;
            return (true, (string?)link.Token);
        }, cancellationToken);

    public Task<EventShareLink?> GetByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query<EventShareLink>()
            .Include(link => link.User)
            .Include(link => link.Event)
            .FirstOrDefaultAsync(link => link.Token == token, ct), cancellationToken);
}
