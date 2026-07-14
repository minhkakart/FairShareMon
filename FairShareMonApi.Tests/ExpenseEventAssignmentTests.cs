using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) for the M6 expense↔event seam woven into the M5
/// repositories: create-into-event (OQ5), assign/move/remove (OQ4/OQ16), whole-day-inclusive UTC range
/// boundaries (OQ1), the expense_time-edit re-validation (OQ1/OQ7), and — the milestone's core §4.4
/// invariant — the closed-event write block on EVERY guarded path
/// (<c>UpdateGeneralInfoAsync</c>/<c>DeleteAsync</c>, share <c>AddAsync</c>/<c>UpdateAsync</c>/
/// <c>DeleteAsync</c>, <c>AssignEventAsync</c>/<c>RemoveEventAsync</c>) with <c>SetSettledAsync</c> as
/// the sole exception, plus the OQ6 "assign/remove writes no audit" rule. Assertions target the typed
/// write status.
/// </summary>
[Collection("AuthIntegration")]
public class ExpenseEventAssignmentTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mid15 = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    // Normalized window for [Day14, Day16]: start=14 00:00:00.000000, end=16 23:59:59.999999.
    private static readonly DateTime StartOfDay14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndOfDay16 = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc).AddTicks(-10);
    private static readonly DateTime JustBeforeStart = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc).AddTicks(-10);
    private static readonly DateTime JustAfterEnd = new(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

    private async Task<Event> NewEventAsync(string userUuid, DateTime? start = null, DateTime? end = null, bool close = false, string name = "Đà Lạt")
    {
        var result = await CreateEventRepository().CreateAsync(userUuid, new CreateEventData(name, null, start ?? Day14, end ?? Day16));
        Assert.Equal(EventWriteStatus.Success, result.Status);
        if (close)
            Assert.Equal(EventWriteStatus.Success, await CreateEventRepository().CloseAsync(userUuid, result.Entity!.Uuid));
        return result.Entity!;
    }

    private async Task<Expense> NewExpenseAsync(Ledger ledger, DateTime expenseTime, string? eventUuid = null, IReadOnlyList<CreateShareData>? shares = null)
    {
        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, expenseTime, null, null, [],
            shares ?? [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], eventUuid));
        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        return result.Entity!;
    }

    private async Task<int> CountAuditAsync(string expenseUuid)
    {
        await using var context = CreateContext();
        return await context.AuditLogs.CountAsync(log => log.ExpenseUuid == expenseUuid);
    }

    // ---- Create-into-event (OQ5) -------------------------------------------------------------------

    [SkippableFact]
    public async Task CreateAsync_IntoOpenEventWithinRange_SetsEventId()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);

        var expense = await NewExpenseAsync(ledger, Mid15, evt.Uuid);

        var persisted = await ReloadExpenseAsync(expense.Uuid);
        Assert.Equal(evt.Id, persisted!.EventId);
    }

    [SkippableFact]
    public async Task CreateAsync_IntoClosedEvent_ReturnsEventClosed()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid, close: true);

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, Mid15, null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Uuid));

        Assert.Equal(ExpenseWriteStatus.EventClosed, result.Status);
        Assert.Equal(0, await CountAuditAsync(result.Entity?.Uuid ?? "")); // nothing persisted
    }

    [SkippableFact]
    public async Task CreateAsync_IntoEventOutOfRange_ReturnsOutOfRange()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, JustAfterEnd, null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Uuid));

        Assert.Equal(ExpenseWriteStatus.ExpenseTimeOutOfEventRange, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_IntoAnotherUsersEvent_ReturnsEventNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var strangerEvent = await NewEventAsync(stranger.User.Uuid);

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, Mid15, null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], strangerEvent.Uuid));

        Assert.Equal(ExpenseWriteStatus.EventNotFound, result.Status); // §4.2 link integrity
    }

    // ---- Assign: within-range boundaries (OQ1) -----------------------------------------------------

    [SkippableFact]
    public async Task AssignEventAsync_ExpenseAtExactStartOfDay_IsInRange()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, StartOfDay14);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, evt.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, result.Status); // start edge inclusive
    }

    [SkippableFact]
    public async Task AssignEventAsync_ExpenseAtExactEndOfDay_IsInRange()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, EndOfDay16);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, evt.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, result.Status); // end edge inclusive (23:59:59.999999)
    }

    [SkippableFact]
    public async Task AssignEventAsync_ExpenseJustBeforeStart_IsOutOfRange()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, JustBeforeStart);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, evt.Uuid);

        Assert.Equal(ExpenseWriteStatus.ExpenseTimeOutOfEventRange, result.Status);
    }

    [SkippableFact]
    public async Task AssignEventAsync_ExpenseJustAfterEnd_IsOutOfRange()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, JustAfterEnd);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, evt.Uuid);

        Assert.Equal(ExpenseWriteStatus.ExpenseTimeOutOfEventRange, result.Status);
    }

    // ---- Assign / move / remove --------------------------------------------------------------------

    [SkippableFact]
    public async Task AssignEventAsync_LooseExpenseWithinRange_SetsEventId()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, Mid15);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, evt.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        Assert.Equal(evt.Id, (await ReloadExpenseAsync(expense.Uuid))!.EventId);
    }

    [SkippableFact]
    public async Task AssignEventAsync_UnknownTargetEvent_ReturnsEventNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await NewExpenseAsync(ledger, Mid15);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, "no-such-event");

        Assert.Equal(ExpenseWriteStatus.EventNotFound, result.Status);
    }

    [SkippableFact]
    public async Task AssignEventAsync_AnotherUsersTargetEvent_ReturnsEventNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var strangerEvent = await NewEventAsync(stranger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, Mid15);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, strangerEvent.Uuid);

        Assert.Equal(ExpenseWriteStatus.EventNotFound, result.Status); // §4.2 link integrity
    }

    [SkippableFact]
    public async Task AssignEventAsync_TargetEventClosed_ReturnsEventClosed()
    {
        var ledger = await SeedLedgerAsync();
        var closed = await NewEventAsync(ledger.User.Uuid, close: true);
        var expense = await NewExpenseAsync(ledger, Mid15);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, closed.Uuid);

        Assert.Equal(ExpenseWriteStatus.EventClosed, result.Status);
    }

    [SkippableFact]
    public async Task AssignEventAsync_MoveBetweenTwoOpenEvents_UpdatesEventId()
    {
        var ledger = await SeedLedgerAsync();
        var eventA = await NewEventAsync(ledger.User.Uuid, name: "A");
        var eventB = await NewEventAsync(ledger.User.Uuid, name: "B");
        var expense = await NewExpenseAsync(ledger, Mid15, eventA.Uuid);

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, eventB.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, result.Status); // direct reassign A -> B (OQ16)
        Assert.Equal(eventB.Id, (await ReloadExpenseAsync(expense.Uuid))!.EventId);
    }

    [SkippableFact]
    public async Task RemoveEventAsync_AssignedExpenseInOpenEvent_GoesLoose()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, Mid15, evt.Uuid);

        var status = await CreateExpenseRepository().RemoveEventAsync(ledger.User.Uuid, expense.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        Assert.Null((await ReloadExpenseAsync(expense.Uuid))!.EventId);
    }

    [SkippableFact]
    public async Task RemoveEventAsync_LooseExpense_IsIdempotentNoOp()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await NewExpenseAsync(ledger, Mid15); // already loose

        var status = await CreateExpenseRepository().RemoveEventAsync(ledger.User.Uuid, expense.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, status); // idempotent (OQ4)
        Assert.Null((await ReloadExpenseAsync(expense.Uuid))!.EventId);
    }

    [SkippableFact]
    public async Task RemoveEventAsync_UnknownExpense_ReturnsExpenseNotFound()
    {
        var ledger = await SeedLedgerAsync();

        var status = await CreateExpenseRepository().RemoveEventAsync(ledger.User.Uuid, "no-such-expense");

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, status);
    }

    // ---- expense_time-edit re-validation (OQ1/OQ7) -------------------------------------------------

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_MovesAssignedExpenseTimeOutOfRange_ReturnsOutOfRange()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, Mid15, evt.Uuid);

        // Edit the expense_time to just after the event's end -> rejected.
        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, expense.Uuid,
            new UpdateExpenseData("Ăn tối", null, JustAfterEnd, null, null, []));

        Assert.Equal(ExpenseWriteStatus.ExpenseTimeOutOfEventRange, result.Status);
        Assert.Equal(Mid15, (await ReloadExpenseAsync(expense.Uuid))!.ExpenseTime); // unchanged
    }

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_KeepsAssignedExpenseTimeInRange_Succeeds()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, Mid15, evt.Uuid);

        var newTime = new DateTime(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc); // still inside [14,16]
        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, expense.Uuid,
            new UpdateExpenseData("Ăn tối", null, newTime, null, null, []));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        Assert.Equal(newTime, (await ReloadExpenseAsync(expense.Uuid))!.ExpenseTime);
    }

    // ---- Closed-event write block (§4.4) - EVERY guarded path -------------------------------------

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_ExpenseInClosedEvent_ReturnsEventClosedAndLeavesItUnchanged()
    {
        var (ledger, _, expense, _) = await SeedClosedEventExpenseAsync();

        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, expense.Uuid,
            new UpdateExpenseData("Đổi tên", null, Mid15, null, null, []));

        Assert.Equal(ExpenseWriteStatus.EventClosed, result.Status);
        Assert.Equal("Ăn tối", (await ReloadExpenseAsync(expense.Uuid))!.Name); // unchanged
    }

    [SkippableFact]
    public async Task DeleteAsync_ExpenseInClosedEvent_ReturnsEventClosedAndKeepsIt()
    {
        var (ledger, _, expense, _) = await SeedClosedEventExpenseAsync();

        var status = await CreateExpenseRepository().DeleteAsync(ledger.User.Uuid, expense.Uuid);

        Assert.Equal(ExpenseWriteStatus.EventClosed, status);
        Assert.NotNull(await ReloadExpenseAsync(expense.Uuid)); // still there
    }

    [SkippableFact]
    public async Task ShareAddAsync_ExpenseInClosedEvent_ReturnsEventClosed()
    {
        var (ledger, _, expense, _) = await SeedClosedEventExpenseAsync();
        var newMember = await SeedMemberAsync(ledger.User.Id, "Bình");

        var result = await CreateShareRepository().AddAsync(ledger.User.Uuid, expense.Uuid,
            new ShareData(newMember.Uuid, 10_000m, null));

        Assert.Equal(ExpenseWriteStatus.EventClosed, result.Status);
    }

    [SkippableFact]
    public async Task ShareUpdateAsync_ExpenseInClosedEvent_ReturnsEventClosed()
    {
        var (ledger, _, expense, friendShareUuid) = await SeedClosedEventExpenseAsync();

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, friendShareUuid,
            new ShareData("ignored", 999_000m, null));

        Assert.Equal(ExpenseWriteStatus.EventClosed, result.Status);
    }

    [SkippableFact]
    public async Task ShareDeleteAsync_ExpenseInClosedEvent_ReturnsEventClosed()
    {
        var (ledger, _, expense, friendShareUuid) = await SeedClosedEventExpenseAsync();

        var status = await CreateShareRepository().DeleteAsync(ledger.User.Uuid, expense.Uuid, friendShareUuid);

        Assert.Equal(ExpenseWriteStatus.EventClosed, status);
    }

    [SkippableFact]
    public async Task RemoveEventAsync_ExpenseInClosedEvent_ReturnsEventClosed()
    {
        var (ledger, _, expense, _) = await SeedClosedEventExpenseAsync();

        var status = await CreateExpenseRepository().RemoveEventAsync(ledger.User.Uuid, expense.Uuid);

        Assert.Equal(ExpenseWriteStatus.EventClosed, status); // can't detach from a closed event (§4.4)
    }

    [SkippableFact]
    public async Task AssignEventAsync_MoveOutOfClosedSource_ReturnsEventClosed()
    {
        var (ledger, _, expense, _) = await SeedClosedEventExpenseAsync();
        var openTarget = await NewEventAsync(ledger.User.Uuid, name: "Open target");

        var result = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, openTarget.Uuid);

        Assert.Equal(ExpenseWriteStatus.EventClosed, result.Status); // can't move out of a closed source (OQ16)
    }

    [SkippableFact]
    public async Task SetSettledAsync_ExpenseInClosedEvent_SucceedsAsTheSoleException()
    {
        var (ledger, _, expense, _) = await SeedClosedEventExpenseAsync();

        var status = await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, expense.Uuid, true);

        Assert.Equal(ExpenseWriteStatus.Success, status); // §4.4 sole exception
        Assert.True((await ReloadExpenseAsync(expense.Uuid))!.IsSettled);
    }

    // ---- No audit for assign/remove (OQ6) ---------------------------------------------------------

    [SkippableFact]
    public async Task AssignAndRemove_WriteNoAuditRows()
    {
        var ledger = await SeedLedgerAsync();
        var eventA = await NewEventAsync(ledger.User.Uuid, name: "A");
        var eventB = await NewEventAsync(ledger.User.Uuid, name: "B");
        var expense = await NewExpenseAsync(ledger, Mid15);
        var baseline = await CountAuditAsync(expense.Uuid); // create-time audit only

        Assert.Equal(ExpenseWriteStatus.Success, (await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, eventA.Uuid)).Status);
        Assert.Equal(ExpenseWriteStatus.Success, (await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, expense.Uuid, eventB.Uuid)).Status);
        Assert.Equal(ExpenseWriteStatus.Success, await CreateExpenseRepository().RemoveEventAsync(ledger.User.Uuid, expense.Uuid));

        Assert.Equal(baseline, await CountAuditAsync(expense.Uuid)); // OQ6: assign/move/remove write no audit
    }

    /// <summary>
    /// Seeds a ledger + a friend member + an OPEN event, creates an expense (owner-rep + friend shares)
    /// into it within range, then closes the event. Returns the friend's share UUID for the share tests.
    /// </summary>
    private async Task<(Ledger Ledger, Event Event, Expense Expense, string FriendShareUuid)> SeedClosedEventExpenseAsync()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var evt = await NewEventAsync(ledger.User.Uuid);
        var expense = await NewExpenseAsync(ledger, Mid15, evt.Uuid, shares:
        [
            new CreateShareData(ledger.OwnerRep.Uuid, 60_000m, null),
            new CreateShareData(friend.Uuid, 40_000m, null)
        ]);

        Assert.Equal(EventWriteStatus.Success, await CreateEventRepository().CloseAsync(ledger.User.Uuid, evt.Uuid));

        var reloaded = await ReloadExpenseAsync(expense.Uuid);
        var friendShareUuid = reloaded!.Shares.Single(share => share.MemberId == friend.Id).Uuid;
        return (ledger, evt, expense, friendShareUuid);
    }
}
