using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

// NOTE: timezone-aware DateTimes (planning/timezone-aware-datetimes.md, D3) added a required
// `TimeZoneInfo Zone` to CreateEventData/UpdateEventData. These tests pin dates as UTC instants and
// assert whole-UTC-day bounds, so they pass TimeZoneInfo.Utc as the zone: normalizing a UTC-day in the
// UTC zone yields exactly those UTC bounds (the tz-aware normalization for a non-UTC zone is covered by
// TimeZoneRangeIntegrationTests and TimeZoneEndpointTests).

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>EventRepository</c> against the real MariaDB (skippable). Covers create +
/// whole-day-inclusive UTC range normalization (OQ1), is_closed=false default, resource-owned scoping
/// (get/update/close/delete misses yield null/EventNotFound, never the row), the default list sort
/// (start_date DESC then created_at DESC) + the closed filter (OQ10), the one-way close (re-close →
/// EventClosed, OQ11), OPEN-only hard delete with its expenses going loose (event_id → null, OQ2/OQ3),
/// closed-event delete rejection (9001), the OQ7 range-edit-excludes-assigned block (9003) + a safe
/// edit, and the DB CHECK <c>ck_events_date_range</c>. Assertions target the typed write status.
/// </summary>
[Collection("AuthIntegration")]
public class EventRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static CreateEventData CreateData(
        string name = "Đà Lạt",
        string? description = null,
        DateTime? startDate = null,
        DateTime? endDate = null) =>
        new(name, description, startDate ?? Day14, endDate ?? Day16, TimeZoneInfo.Utc);

    private async Task<int> CountEventsAsync(ulong userId)
    {
        await using var context = CreateContext();
        return await context.Events.CountAsync(evt => evt.UserId == userId);
    }

    // ---- Create + range normalization --------------------------------------------------------------

    [SkippableFact]
    public async Task CreateAsync_SetsUuidUserIdOpenState_AndNormalizesRangeToWholeUtcDays()
    {
        var user = await SeedUserAsync();

        var result = await CreateEventRepository().CreateAsync(user.Uuid,
            CreateData(startDate: new DateTime(2026, 7, 14, 9, 30, 0, DateTimeKind.Utc),
                       endDate: new DateTime(2026, 7, 16, 18, 45, 0, DateTimeKind.Utc)));

        Assert.Equal(EventWriteStatus.Success, result.Status);
        var persisted = await ReloadEventAsync(result.Entity!.Uuid);
        Assert.NotNull(persisted);
        Assert.False(string.IsNullOrEmpty(persisted!.Uuid));
        Assert.Equal(user.Id, persisted.UserId);
        Assert.False(persisted.IsClosed);
        Assert.Null(persisted.ClosedAt);
        // Whole-day-inclusive UTC window (OQ1): start -> 00:00:00.000000, end -> 23:59:59.999999.
        Assert.Equal(new DateTime(2026, 7, 14, 0, 0, 0), persisted.StartDate);
        Assert.Equal(new DateTime(2026, 7, 17, 0, 0, 0).AddTicks(-10), persisted.EndDate);
    }

    [SkippableFact]
    public async Task CreateAsync_UnknownUser_ReturnsEventNotFound()
    {
        var result = await CreateEventRepository().CreateAsync("00000000-0000-7000-8000-000000000000", CreateData());

        Assert.Equal(EventWriteStatus.EventNotFound, result.Status);
    }

    // ---- Resource-owned scoping --------------------------------------------------------------------

    [SkippableFact]
    public async Task GetByUuidAsync_AnotherUsersEvent_ReturnsNull()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());

        var seenByStranger = await CreateEventRepository().GetByUuidAsync(stranger.Uuid, created.Entity!.Uuid);
        var seenByOwner = await CreateEventRepository().GetByUuidAsync(owner.Uuid, created.Entity.Uuid);

        Assert.Null(seenByStranger); // existence not leaked
        Assert.NotNull(seenByOwner);
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsOnlyTheCallersEvents()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Mine"));
        await CreateEventRepository().CreateAsync(stranger.Uuid, CreateData(name: "Theirs"));

        var list = await CreateEventRepository().ListByUserAsync(owner.Uuid, new());

        Assert.Equal(["Mine"], list.Select(evt => evt.Name));
    }

    [SkippableFact]
    public async Task UpdateAsync_AnotherUsersEvent_ReturnsEventNotFoundAndLeavesItIntact()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Mine"));

        var result = await CreateEventRepository().UpdateAsync(stranger.Uuid, created.Entity!.Uuid,
            new UpdateEventData("Hacked", null, Day14, Day16, TimeZoneInfo.Utc));

        Assert.Equal(EventWriteStatus.EventNotFound, result.Status);
        Assert.Equal("Mine", (await ReloadEventAsync(created.Entity.Uuid))!.Name); // untouched
    }

    [SkippableFact]
    public async Task CloseAsync_AnotherUsersEvent_ReturnsEventNotFoundAndLeavesItOpen()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());

        var status = await CreateEventRepository().CloseAsync(stranger.Uuid, created.Entity!.Uuid);

        Assert.Equal(EventWriteStatus.EventNotFound, status);
        Assert.False((await ReloadEventAsync(created.Entity.Uuid))!.IsClosed);
    }

    [SkippableFact]
    public async Task DeleteAsync_AnotherUsersEvent_ReturnsEventNotFoundAndLeavesItIntact()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());

        var status = await CreateEventRepository().DeleteAsync(stranger.Uuid, created.Entity!.Uuid);

        Assert.Equal(EventWriteStatus.EventNotFound, status);
        Assert.NotNull(await ReloadEventAsync(created.Entity.Uuid));
    }

    [SkippableFact]
    public async Task GetByUuidAsync_UnknownUuid_ReturnsNull()
    {
        var owner = await SeedUserAsync();

        var loaded = await CreateEventRepository().GetByUuidAsync(owner.Uuid, "no-such-uuid");

        Assert.Null(loaded);
    }

    // ---- List: sort + closed filter ----------------------------------------------------------------

    [SkippableFact]
    public async Task ListByUserAsync_SortsByStartDateDescending()
    {
        var owner = await SeedUserAsync();
        await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Older", startDate: Day14.AddDays(-5), endDate: Day14.AddDays(-4)));
        await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Newer", startDate: Day14, endDate: Day16));
        await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Middle", startDate: Day14.AddDays(-2), endDate: Day14.AddDays(-1)));

        var list = await CreateEventRepository().ListByUserAsync(owner.Uuid, new());

        Assert.Equal(["Newer", "Middle", "Older"], list.Select(evt => evt.Name)); // start_date DESC (OQ10)
    }

    [SkippableFact]
    public async Task ListByUserAsync_ClosedFilter_ReturnsOnlyMatchingState()
    {
        var owner = await SeedUserAsync();
        var open = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Open", startDate: Day14, endDate: Day16));
        var toClose = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData(name: "Closed", startDate: Day14.AddDays(-1), endDate: Day16));
        await CreateEventRepository().CloseAsync(owner.Uuid, toClose.Entity!.Uuid);

        var closedOnly = await CreateEventRepository().ListByUserAsync(owner.Uuid, new() { Closed = true });
        var openOnly = await CreateEventRepository().ListByUserAsync(owner.Uuid, new() { Closed = false });

        Assert.Equal(["Closed"], closedOnly.Select(evt => evt.Name));
        Assert.Equal(["Open"], openOnly.Select(evt => evt.Name));
    }

    [SkippableFact]
    public async Task ListByUserAsync_ProjectsDerivedExpenseCount()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await CreateEventRepository().CreateAsync(ledger.User.Uuid, CreateData(startDate: Day14, endDate: Day16));
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Entity!.Uuid));

        var list = await CreateEventRepository().ListByUserAsync(ledger.User.Uuid, new());

        Assert.Single(Assert.Single(list).Expenses);
    }

    // ---- One-way close -----------------------------------------------------------------------------

    [SkippableFact]
    public async Task CloseAsync_SetsIsClosedAndClosedAt()
    {
        var owner = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());

        var status = await CreateEventRepository().CloseAsync(owner.Uuid, created.Entity!.Uuid);

        Assert.Equal(EventWriteStatus.Success, status);
        var persisted = await ReloadEventAsync(created.Entity.Uuid);
        Assert.True(persisted!.IsClosed);
        Assert.NotNull(persisted.ClosedAt);
    }

    [SkippableFact]
    public async Task CloseAsync_AlreadyClosed_ReturnsEventClosed()
    {
        var owner = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());
        await CreateEventRepository().CloseAsync(owner.Uuid, created.Entity!.Uuid);

        var reclose = await CreateEventRepository().CloseAsync(owner.Uuid, created.Entity.Uuid);

        Assert.Equal(EventWriteStatus.EventClosed, reclose); // one-way (OQ11)
    }

    // ---- Delete: OPEN-only hard delete; expenses go loose ------------------------------------------

    [SkippableFact]
    public async Task DeleteAsync_OpenEvent_HardDeletesAndLoosensItsExpenses()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await CreateEventRepository().CreateAsync(ledger.User.Uuid, CreateData(startDate: Day14, endDate: Day16));
        var expense = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Entity!.Uuid));

        var status = await CreateEventRepository().DeleteAsync(ledger.User.Uuid, evt.Entity.Uuid);

        Assert.Equal(EventWriteStatus.Success, status);
        Assert.Equal(0, await CountEventsAsync(ledger.User.Id)); // hard-deleted
        var loosened = await ReloadExpenseAsync(expense.Entity!.Uuid);
        Assert.NotNull(loosened); // OQ2: the expense survives...
        Assert.Null(loosened!.EventId); // ...and goes loose (ON DELETE SET NULL)
    }

    [SkippableFact]
    public async Task DeleteAsync_ClosedEvent_ReturnsEventClosedAndKeepsIt()
    {
        var owner = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());
        await CreateEventRepository().CloseAsync(owner.Uuid, created.Entity!.Uuid);

        var status = await CreateEventRepository().DeleteAsync(owner.Uuid, created.Entity.Uuid);

        Assert.Equal(EventWriteStatus.EventClosed, status); // delete only while OPEN (OQ3)
        Assert.NotNull(await ReloadEventAsync(created.Entity.Uuid));
    }

    [SkippableFact]
    public async Task DeleteAsync_UnknownUuid_ReturnsEventNotFound()
    {
        var owner = await SeedUserAsync();

        var status = await CreateEventRepository().DeleteAsync(owner.Uuid, "no-such-uuid");

        Assert.Equal(EventWriteStatus.EventNotFound, status);
    }

    // ---- Update: range-edit-excludes-assigned (OQ7) ------------------------------------------------

    [SkippableFact]
    public async Task UpdateAsync_NewRangeExcludesAssignedExpense_ReturnsRangeConflict()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await CreateEventRepository().CreateAsync(ledger.User.Uuid, CreateData(startDate: Day14, endDate: Day16));
        // Assign an expense on the 15th (inside [14,16]).
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Entity!.Uuid));

        // Shrink the range to [17,18] - would leave the 15th expense out of range.
        var result = await CreateEventRepository().UpdateAsync(ledger.User.Uuid, evt.Entity.Uuid,
            new UpdateEventData("Đà Lạt", null, new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc));

        Assert.Equal(EventWriteStatus.RangeExcludesAssignedExpenses, result.Status); // OQ7
        // The range is unchanged (invariant preserved).
        var persisted = await ReloadEventAsync(evt.Entity.Uuid);
        Assert.Equal(new DateTime(2026, 7, 14, 0, 0, 0), persisted!.StartDate);
    }

    [SkippableFact]
    public async Task UpdateAsync_SafeRangeEditThatStillContainsAssigned_Persists()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await CreateEventRepository().CreateAsync(ledger.User.Uuid, CreateData(startDate: Day14, endDate: Day16));
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Entity!.Uuid));

        // Widen the range to [14,20] - still contains the 15th expense.
        var result = await CreateEventRepository().UpdateAsync(ledger.User.Uuid, evt.Entity.Uuid,
            new UpdateEventData("Đà Lạt dài", "6 ngày", Day14, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc));

        Assert.Equal(EventWriteStatus.Success, result.Status);
        var persisted = await ReloadEventAsync(evt.Entity.Uuid);
        Assert.Equal("Đà Lạt dài", persisted!.Name);
        Assert.Equal(new DateTime(2026, 7, 21, 0, 0, 0).AddTicks(-10), persisted.EndDate); // widened + normalized
    }

    [SkippableFact]
    public async Task UpdateAsync_ClosedEvent_ReturnsEventClosed()
    {
        var owner = await SeedUserAsync();
        var created = await CreateEventRepository().CreateAsync(owner.Uuid, CreateData());
        await CreateEventRepository().CloseAsync(owner.Uuid, created.Entity!.Uuid);

        var result = await CreateEventRepository().UpdateAsync(owner.Uuid, created.Entity.Uuid,
            new UpdateEventData("Sửa", null, Day14, Day16, TimeZoneInfo.Utc));

        Assert.Equal(EventWriteStatus.EventClosed, result.Status);
    }

    // ---- DB CHECK ck_events_date_range -------------------------------------------------------------

    [SkippableFact]
    public async Task Events_EndBeforeStartInsert_RejectedByDbCheckConstraint()
    {
        var user = await SeedUserAsync();

        await using var context = CreateContext();
        context.Events.Add(new Event
        {
            UserId = user.Id,
            Name = "Ngược ngày",
            StartDate = new DateTime(2026, 7, 16, 0, 0, 0),
            EndDate = new DateTime(2026, 7, 14, 0, 0, 0) // end < start
        });

        // ck_events_date_range rejects the inverted range at the DB (the codebase's 2nd CHECK, OQ1).
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
