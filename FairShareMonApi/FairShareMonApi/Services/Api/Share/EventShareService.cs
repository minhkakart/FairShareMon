using System.Security.Cryptography;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Share;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Services.Api.Tiers;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Utils;
using FluentValidation;
using Microsoft.AspNetCore.WebUtilities;

namespace FairShareMonApi.Services.Api.Share;

/// <summary>
/// Business logic for public, read-only, 1-day-TTL share links of CLOSED events
/// (planning/event-share-link.md). Creation is Premium-gated (§3.11) and closed-only (§4.4, via the
/// M7 balance <c>IsClosed</c>); the owner can view/copy the active link, revoke it, and regenerate it,
/// and creating while an unexpired non-revoked link exists reuses it (Decision 4). The anonymous
/// public read/QR is <b>never</b> re-gated (§4 rule 9) and is a LIVE read: the closed event's spend
/// figures are frozen but the settled/outstanding overlay reflects current state on every call. The
/// token is an opaque CSPRNG value stored plain (re-displayable, Decision 6); the bank destination is
/// snapshotted at creation (Decision 7) and is optional (OQ4b -&gt; <c>HasQr</c>).
/// </summary>
public interface IEventShareService
{
    /// <summary>Creates (or reuses/regenerates) the owner's share link for a closed event. Premium-gated + closed-only.</summary>
    Task<ShareLinkResponse> CreateAsync(string userUuid, string eventUuid, CreateShareLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>The owner's active share link for the event, or null when none exists (OQ8a). Ownership miss -&gt; 9000.</summary>
    Task<ShareLinkResponse?> GetActiveAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>Revokes the owner's active share link for the event (idempotent). Ownership miss -&gt; 9000.</summary>
    Task RevokeAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>[Anonymous] The LIVE public report for a token. Unknown/expired/revoked -&gt; 16000.</summary>
    Task<PublicEventShareResponse> GetPublicAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>[Anonymous] Per-member VietQR images for a token (empty when no snapshot or nobody owes). Unknown/expired/revoked -&gt; 16000.</summary>
    Task<IReadOnlyList<MemberQrResponse>> GetPublicMemberQrsAsync(string token, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IEventShareService))]
public sealed class EventShareService(
    IEventShareLinkRepository shareLinkRepository,
    IEventShareLinkCache shareLinkCache,
    IStatsService statsService,
    IEventsService eventsService,
    IExpensesService expensesService,
    IBankAccountRepository bankAccountRepository,
    IWalletQrService walletQrService,
    ITierService tierService,
    IValidator<CreateShareLinkRequest> createValidator,
    IConfiguration configuration) : IEventShareService
{
    private const int TokenByteLength = 32;

    private readonly TimeSpan _linkTtl = TimeSpan.FromHours(configuration.GetValue("Share:LinkTtlHours", 24));

    public async Task<ShareLinkResponse> CreateAsync(string userUuid, string eventUuid, CreateShareLinkRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        // Premium gate first (§3.11): a Free caller gets 403 13003 before anything is resolved.
        tierService.EnsurePremiumFeature(MessageKeys.Feature.Share);

        // Resource-owned (miss -> EventNotFound 9000); also the source of the closed-only invariant.
        var balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, cancellationToken);

        // Closed-only creation (§4.4), mirroring the closed-only event QR.
        if (!balance.IsClosed)
            throw new ErrorException(ErrorCodes.EventNotClosedForShare, MessageKeys.Error.EventNotClosedForShare);

        if (request.Regenerate)
        {
            // Regenerate (OQ5b): revoke the active link + mint a fresh one.
            var (revoked, oldToken) = await shareLinkRepository.RevokeActiveByEventAsync(userUuid, eventUuid, cancellationToken);
            if (revoked && oldToken is not null)
                await shareLinkCache.RemoveAsync(oldToken, cancellationToken);
        }
        else
        {
            // Reuse an unexpired non-revoked link unchanged (Decision 4, OQ6a), ignoring a differing
            // bankAccountUuid; the fixed TTL is preserved (OQ7a - MapToResponse returns the stored ExpiresAt).
            var active = await shareLinkRepository.GetActiveByEventAsync(userUuid, eventUuid, cancellationToken);
            if (active is not null)
                return MapToResponse(active);
        }

        // Resolve + snapshot the destination bank (OQ4b): explicit override (miss -> 12000), else the
        // default account if one exists, else no snapshot at all (hasQr = false).
        BankAccount? account;
        if (!string.IsNullOrWhiteSpace(request.BankAccountUuid))
        {
            account = await bankAccountRepository.GetByUuidAsync(userUuid, request.BankAccountUuid, cancellationToken)
                ?? throw new ErrorException(ErrorCodes.BankAccountNotFound, MessageKeys.Error.BankAccountNotFound);
        }
        else
        {
            account = await bankAccountRepository.GetDefaultAsync(userUuid, cancellationToken);
        }

        var token = NewRawToken();
        var expiresAt = AppDateTime.Now + _linkTtl;

        var link = await shareLinkRepository.CreateAsync(
            userUuid,
            eventUuid,
            token,
            expiresAt,
            account?.Uuid,
            account?.BankBin,
            account?.BankName,
            account?.AccountNumber,
            account?.AccountHolderName,
            cancellationToken);

        // Post-commit side-effect: prime the cache (validation self-heals via DB fallback anyway).
        await shareLinkCache.AddAsync(
            token,
            new EventShareLinkEntry(
                userUuid,
                eventUuid,
                link.ExpiresAt,
                link.BankAccountUuid,
                link.BankBin,
                link.BankName,
                link.AccountNumber,
                link.AccountHolderName),
            cancellationToken);

        return MapToResponse(link);
    }

    public async Task<ShareLinkResponse?> GetActiveAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        // Ownership check (resource-owned, miss -> 9000).
        _ = await eventsService.GetAsync(userUuid, eventUuid, cancellationToken);

        var active = await shareLinkRepository.GetActiveByEventAsync(userUuid, eventUuid, cancellationToken);
        return active is null ? null : MapToResponse(active);
    }

    public async Task RevokeAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        // Ownership check (resource-owned, miss -> 9000).
        _ = await eventsService.GetAsync(userUuid, eventUuid, cancellationToken);

        var (revoked, token) = await shareLinkRepository.RevokeActiveByEventAsync(userUuid, eventUuid, cancellationToken);
        if (revoked && token is not null)
            await shareLinkCache.RemoveAsync(token, cancellationToken); // post-commit cache eviction
    }

    public async Task<PublicEventShareResponse> GetPublicAsync(string token, CancellationToken cancellationToken = default)
    {
        var entry = await shareLinkCache.LookupAsync(token, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.ShareLinkNotFoundOrExpired, MessageKeys.Error.ShareLinkNotFoundOrExpired);

        // LIVE read using the OWNER uuid from the entry (never the anonymous caller).
        var evt = await eventsService.GetAsync(entry.OwnerUserUuid, entry.EventUuid, cancellationToken);
        var balance = await statsService.GetEventBalanceAsync(entry.OwnerUserUuid, entry.EventUuid, cancellationToken);
        var expenses = await expensesService.ListDetailedByEventAsync(entry.OwnerUserUuid, entry.EventUuid, cancellationToken);

        return new PublicEventShareResponse
        {
            EventName = evt.Name,
            ClosedAt = evt.ClosedAt,
            Rows = balance.Rows,
            Expenses = expenses.Select(MapExpense).ToList(),
            TotalOutstanding = balance.TotalOutstanding,
            OwingMemberCount = balance.OwingMemberCount,
            SettledMemberCount = balance.SettledMemberCount,
            HasQr = entry.BankBin is not null
        };
    }

    public async Task<IReadOnlyList<MemberQrResponse>> GetPublicMemberQrsAsync(string token, CancellationToken cancellationToken = default)
    {
        var entry = await shareLinkCache.LookupAsync(token, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.ShareLinkNotFoundOrExpired, MessageKeys.Error.ShareLinkNotFoundOrExpired);

        // No bank snapshot (OQ4b) -> no QR to render.
        if (entry.BankBin is null)
            return [];

        var snapshot = new BankSnapshot(
            entry.BankBin,
            entry.BankName ?? string.Empty,
            entry.AccountNumber ?? string.Empty,
            entry.AccountHolderName ?? string.Empty);

        return await walletQrService.GenerateEventMemberQrsForShareAsync(entry.OwnerUserUuid, entry.EventUuid, snapshot, cancellationToken);
    }

    private static ShareLinkResponse MapToResponse(EventShareLink link) => new()
    {
        Token = link.Token,
        ExpiresAt = link.ExpiresAt,
        CreatedAt = link.CreatedAt,
        HasQr = link.BankBin is not null,
        BankName = link.BankName,
        AccountNumber = link.AccountNumber,
        AccountHolderName = link.AccountHolderName
    };

    private static PublicExpense MapExpense(ExpenseResponse expense) => new()
    {
        Uuid = expense.Uuid,
        Name = expense.Name,
        PayerMemberUuid = expense.Payer.Uuid,
        PayerName = expense.Payer.Name,
        ExpenseTime = expense.ExpenseTime,
        Total = expense.Total,
        Shares = expense.Shares.Select(share => new PublicShare
        {
            MemberUuid = share.Member.Uuid,
            MemberName = share.Member.Name,
            Amount = share.Amount,
            IsSettled = share.IsSettled,
            Note = share.Note
        }).ToList()
    };

    /// <summary>Mints an opaque 256-bit URL-safe token (identical to TokenService.NewRawToken, OQ3b).</summary>
    private static string NewRawToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenByteLength));
}
