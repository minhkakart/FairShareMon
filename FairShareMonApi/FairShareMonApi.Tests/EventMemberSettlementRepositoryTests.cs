using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>EventMemberSettlementRepository</c> (Layer B per-member-per-event net
/// clearance, settled-per-member §3.7/§6) against the real MariaDB (skippable). Proves the upsert on the
/// composite <c>(event_id, member_id)</c> key (mark / re-mark idempotent / unmark), participant-only
/// resolution (a non-participant or foreign member → <c>MemberNotFound</c>; a foreign/unknown event →
/// <c>EventNotFound</c>), OQ5a (allowed on a CLOSED event), §4.7 (a soft-deleted participant is still
/// markable), and the FK cascade that drops the settlement rows when the event is deleted. All
/// participation data is seeded directly so the exact payer/share membership is pinned.
/// </summary>
[Collection("AuthIntegration")]
public class EventMemberSettlementRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private EventMemberSettlementRepository CreateSettlementRepository() => new(CreateContext());

    /// <summary>Seeds an expense in the event making <paramref name="payerMemberId"/> and every share member a participant.</summary>
    private async Task SeedEventExpenseAsync(ulong userId, ulong payerMemberId, ulong categoryId, ulong eventId, params (ulong MemberId, decimal Amount)[] shares)
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
    }

    private async Task<EventMemberSettlement?> SettlementAsync(ulong eventId, ulong memberId)
    {
        await using var context = CreateContext();
        return await context.EventMemberSettlements.AsNoTracking()
            .SingleOrDefaultAsync(s => s.EventId == eventId && s.MemberId == memberId);
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_Participant_UpsertsSettledRow()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 0m), (binh.Id, 500_000m)); // Bình participates (share-holder)

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status);
        var row = await SettlementAsync(evt.Id, binh.Id);
        Assert.NotNull(row);
        Assert.True(row!.IsSettled);
        Assert.NotNull(row.SettledAt);
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_ReMark_IsIdempotentSingleRow()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);
        await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        await using var context = CreateContext();
        Assert.Equal(1, await context.EventMemberSettlements.CountAsync(s => s.EventId == evt.Id && s.MemberId == binh.Id)); // composite-PK upsert, not a second row
        Assert.True((await SettlementAsync(evt.Id, binh.Id))!.IsSettled);
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_Unmark_ClearsFlagAndSettledAt()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);
        await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: false);

        var row = await SettlementAsync(evt.Id, binh.Id);
        Assert.NotNull(row);
        Assert.False(row!.IsSettled);
        Assert.Null(row.SettledAt);
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_NonParticipantMember_ReturnsMemberNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var outsider = await SeedMemberAsync(ledger.User.Id, "Không tham gia"); // owned but never in the event
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, outsider.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.MemberNotFound, status); // OQ9a
        Assert.Null(await SettlementAsync(evt.Id, outsider.Id));
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_ForeignMember_ReturnsMemberNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedUserAsync();
        var strangerMember = await SeedMemberAsync(stranger.Id, "Ngoài");
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, strangerMember.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.MemberNotFound, status); // resource-owned: another user's member never leaks
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_ForeignEvent_ReturnsEventNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var evt = await SeedEventAsync(ledger.User.Id, "Của tôi", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (ledger.OwnerRep.Id, 100_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(stranger.User.Uuid, evt.Uuid, stranger.OwnerRep.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.EventNotFound, status); // event resolved first, scoped to the caller
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_ClosedEvent_Succeeds()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Chốt", Day14, Day16, closed: true);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status); // Layer B is primarily a post-close action (OQ5a)
    }

    [SkippableFact]
    public async Task SetMemberSettledAsync_SoftDeletedParticipant_IsStillMarkable()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình", deleted: true); // soft-deleted but still owing
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        var status = await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);

        Assert.Equal(SettlementWriteStatus.Success, status); // resolved with includeDeleted:true (§4.7)
        Assert.True((await SettlementAsync(evt.Id, binh.Id))!.IsSettled);
    }

    [SkippableFact]
    public async Task DeletingEvent_CascadesAwaySettlementRows()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));
        await CreateSettlementRepository().SetMemberSettledAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, isSettled: true);
        Assert.NotNull(await SettlementAsync(evt.Id, binh.Id));

        await using (var context = CreateContext())
        {
            // Remove the expense first (its RESTRICT member FK would otherwise block), then the event.
            await context.Expenses.Where(e => e.EventId == evt.Id).ExecuteDeleteAsync();
            await context.Events.Where(e => e.Id == evt.Id).ExecuteDeleteAsync();
        }

        Assert.Null(await SettlementAsync(evt.Id, binh.Id)); // event_id FK cascade dropped the row
    }
}
