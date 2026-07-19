using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Validators.Events;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>EventsService</c> over a fake <see cref="IEventRepository"/> plus the real
/// AutoMapper profile and validators (no DB). Proves: create maps the repo's typed write result to a
/// response (with derived expenseCount); get/update/close/delete misses → 9000; update/close/delete on
/// a closed event → 9001; a range-edit conflict → 9003 (OQ7); a validation failure throws before
/// touching the repo. Assertions target stable error CODES.
/// </summary>
public class EventsServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000000e6";

    private readonly FakeEventRepository _events = new();
    private readonly FakeTierService _tier = new();

    // Timezone-aware DateTimes (D3): EventsService now injects IRequestTimeZone and threads its Zone into
    // CreateEventData/UpdateEventData. The repo is faked here (no normalization runs), so the stubbed zone
    // only needs to prove the service reads and forwards it - see CreateAsync_PassesRequestZoneToRepository.
    private readonly TestRequestTimeZone _requestTimeZone = new(TestTimeZones.Plus7);

    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<EventProfile>()).CreateMapper();

    private EventsService CreateService() =>
        new(_events, _tier, _requestTimeZone, _mapper, new CreateEventRequestValidator(), new UpdateEventRequestValidator());

    private static readonly DateTime Start = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 16, 23, 59, 59, DateTimeKind.Utc);

    // Effective-updatedAt timestamps for the derived fields (planning/event-summary-advanced-and-updated.md,
    // Option B): the event row's own bump vs. the latest child activity. LatestChildActivity is after
    // EventUpdated so StoredEvent() exercises the "a child is newer than the event row" branch.
    private static readonly DateTime EventUpdated = new(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LatestChildActivity = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

    private static CreateEventRequest CreateRequest(string name = "Đà Lạt") =>
        new() { Name = name, StartDate = Start, EndDate = End };

    private static UpdateEventRequest UpdateRequest(string name = "Đà Lạt 2") =>
        new() { Name = name, StartDate = Start, EndDate = End };

    // Two expenses whose shares sum to 900 (800 + 100), with child timestamps that peak at
    // LatestChildActivity (later than the event row's EventUpdated). ExpenseCount stays 2 so the existing
    // assertions keep passing; the added share data + timestamps feed the derived totalAdvanced/updatedAt.
    private static Event StoredEvent()
    {
        var evt = new Event { Name = "Đà Lạt", StartDate = Start, EndDate = End, UpdatedAt = EventUpdated };

        var dinner = new Expense { Name = "Ăn tối", ExpenseTime = Start, UpdatedAt = EventUpdated };
        dinner.Shares.Add(new Share { Amount = 200m, UpdatedAt = EventUpdated });
        dinner.Shares.Add(new Share { Amount = 200m, UpdatedAt = EventUpdated });
        dinner.Shares.Add(new Share { Amount = 200m, UpdatedAt = EventUpdated });
        dinner.Shares.Add(new Share { Amount = 200m, UpdatedAt = LatestChildActivity }); // newest child
        evt.Expenses.Add(dinner);

        var coffee = new Expense { Name = "Cà phê", ExpenseTime = Start, UpdatedAt = EventUpdated };
        coffee.Shares.Add(new Share { Amount = 50m, UpdatedAt = EventUpdated });
        coffee.Shares.Add(new Share { Amount = 50m, UpdatedAt = EventUpdated });
        evt.Expenses.Add(coffee);

        return evt;
    }

    // ---- Create ------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Success_ReturnsMappedResponseWithDerivedExpenseCount()
    {
        var evt = StoredEvent();
        _events.StoredEvent = evt;
        _events.CreateResult = EventWriteResult<Event>.Success(evt);

        var response = await CreateService().CreateAsync(UserUuid, CreateRequest());

        Assert.Equal(evt.Uuid, response.Uuid);
        Assert.Equal("Đà Lạt", response.Name);
        Assert.False(response.IsClosed);
        Assert.Equal(2, response.ExpenseCount); // derived from Expenses (OQ9/OQ15)
    }

    [Fact]
    public async Task CreateAsync_TrimsNameAndPassesDataToRepository()
    {
        var evt = StoredEvent();
        _events.StoredEvent = evt;
        _events.CreateResult = EventWriteResult<Event>.Success(evt);

        var request = CreateRequest("   Đà Lạt   ");
        await CreateService().CreateAsync(UserUuid, request);

        Assert.Equal("Đà Lạt", _events.LastCreateData!.Name);
    }

    [Fact]
    public async Task CreateAsync_PassesRequestZoneToRepository()
    {
        var evt = StoredEvent();
        _events.StoredEvent = evt;
        _events.CreateResult = EventWriteResult<Event>.Success(evt);
        _requestTimeZone.Zone = TestTimeZones.Minus5;

        await CreateService().CreateAsync(UserUuid, CreateRequest());

        // The service forwards IRequestTimeZone.Zone verbatim so the repository normalizes the whole-day
        // range in the viewer's zone (D3).
        Assert.Equal(TestTimeZones.Minus5, _events.LastCreateData!.Zone);
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_ThrowsValidationExceptionAndSkipsRepository()
    {
        var request = CreateRequest();
        request.Name = "";

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => CreateService().CreateAsync(UserUuid, request));

        Assert.Null(_events.LastCreateData); // repo never called
    }

    [Fact]
    public async Task CreateAsync_OpenEventLimitReached_ThrowsOpenEventLimitReached13001()
    {
        _tier.OpenEventLimitCode = ErrorCodes.OpenEventLimitReached; // M10: Free caller at the open-event cap

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().CreateAsync(UserUuid, CreateRequest()));

        Assert.Equal(ErrorCodes.OpenEventLimitReached, exception.Code);
        Assert.Null(_events.LastCreateData); // guard fires before the repository create
    }

    [Fact]
    public async Task CreateAsync_UnknownUser_ThrowsEventNotFound9000()
    {
        _events.CreateResult = EventWriteResult<Event>.Fail(EventWriteStatus.EventNotFound);

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().CreateAsync(UserUuid, CreateRequest()));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    // ---- Get ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_Miss_ThrowsEventNotFound9000()
    {
        _events.StoredEvent = null;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().GetAsync(UserUuid, "no-such"));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsMappedResponse()
    {
        var evt = StoredEvent();
        _events.StoredEvent = evt;

        var response = await CreateService().GetAsync(UserUuid, evt.Uuid);

        Assert.Equal(evt.Uuid, response.Uuid);
        Assert.Equal(2, response.ExpenseCount);
    }

    [Fact]
    public async Task GetAsync_MapsTotalAdvancedAndEffectiveUpdatedAt()
    {
        var evt = StoredEvent(); // 900; newest child at LatestChildActivity
        _events.StoredEvent = evt;

        var response = await CreateService().GetAsync(UserUuid, evt.Uuid);

        Assert.Equal(900m, response.TotalAdvanced);
        Assert.Equal(LatestChildActivity, response.UpdatedAt);
    }

    [Fact]
    public async Task GetAsync_TotalAdvancedZeroAndUpdatedAtFromEvent_WhenNoExpenses()
    {
        var evt = new Event { Name = "Rỗng", StartDate = Start, EndDate = End, UpdatedAt = EventUpdated };
        _events.StoredEvent = evt;

        var response = await CreateService().GetAsync(UserUuid, evt.Uuid);

        Assert.Equal(0m, response.TotalAdvanced);
        Assert.Equal(EventUpdated, response.UpdatedAt);
    }

    // ---- Update ------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_Success_ReturnsResponse()
    {
        var evt = StoredEvent();
        _events.StoredEvent = evt;
        _events.UpdateStatus = EventWriteStatus.Success;

        var response = await CreateService().UpdateAsync(UserUuid, evt.Uuid, UpdateRequest());

        Assert.Equal(evt.Uuid, response.Uuid);
    }

    [Fact]
    public async Task UpdateAsync_Miss_ThrowsEventNotFound9000()
    {
        _events.UpdateStatus = EventWriteStatus.EventNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "no-such", UpdateRequest()));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_ClosedEvent_ThrowsEventClosed9001()
    {
        _events.UpdateStatus = EventWriteStatus.EventClosed;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "e-1", UpdateRequest()));

        Assert.Equal(ErrorCodes.EventClosed, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_RangeExcludesAssignedExpense_ThrowsRangeConflict9003()
    {
        _events.UpdateStatus = EventWriteStatus.RangeExcludesAssignedExpenses;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "e-1", UpdateRequest()));

        Assert.Equal(ErrorCodes.EventRangeExcludesAssignedExpenses, exception.Code); // OQ7
    }

    [Fact]
    public async Task UpdateAsync_InvalidRequest_ThrowsValidationException()
    {
        var request = UpdateRequest();
        request.Name = "";

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().UpdateAsync(UserUuid, "e-1", request));
    }

    // ---- Close -------------------------------------------------------------------------------------

    [Fact]
    public async Task CloseAsync_Success_ThrowsNothing()
    {
        _events.CloseStatus = EventWriteStatus.Success;

        await CreateService().CloseAsync(UserUuid, "e-1");
    }

    [Fact]
    public async Task CloseAsync_Miss_ThrowsEventNotFound9000()
    {
        _events.CloseStatus = EventWriteStatus.EventNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().CloseAsync(UserUuid, "no-such"));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task CloseAsync_AlreadyClosed_ThrowsEventClosed9001()
    {
        _events.CloseStatus = EventWriteStatus.EventClosed;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().CloseAsync(UserUuid, "e-1"));

        Assert.Equal(ErrorCodes.EventClosed, exception.Code); // one-way: re-close rejected (OQ11)
    }

    // ---- Delete ------------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_Success_ThrowsNothing()
    {
        _events.DeleteStatus = EventWriteStatus.Success;

        await CreateService().DeleteAsync(UserUuid, "e-1");
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsEventNotFound9000()
    {
        _events.DeleteStatus = EventWriteStatus.EventNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().DeleteAsync(UserUuid, "no-such"));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_ClosedEvent_ThrowsEventClosed9001()
    {
        _events.DeleteStatus = EventWriteStatus.EventClosed;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().DeleteAsync(UserUuid, "e-1"));

        Assert.Equal(ErrorCodes.EventClosed, exception.Code); // delete only while OPEN (OQ3)
    }

    // ---- List --------------------------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_MapsEventsToSummaryResponses()
    {
        _events.StoredEvent = StoredEvent();

        var list = await CreateService().ListAsync(UserUuid, new EventFilter());

        var summary = Assert.Single(list);
        Assert.Equal("Đà Lạt", summary.Name);
        Assert.Equal(2, summary.ExpenseCount);
    }

    [Fact]
    public async Task ListAsync_MapsTotalAdvanced_FromSumOfSharesAcrossEventExpenses()
    {
        _events.StoredEvent = StoredEvent(); // 800 + 100

        var summary = Assert.Single(await CreateService().ListAsync(UserUuid, new EventFilter()));

        Assert.Equal(900m, summary.TotalAdvanced);
    }

    [Fact]
    public async Task ListAsync_TotalAdvanced_IsZero_WhenNoExpenses()
    {
        _events.StoredEvent = new Event { Name = "Rỗng", StartDate = Start, EndDate = End, UpdatedAt = EventUpdated };

        var summary = Assert.Single(await CreateService().ListAsync(UserUuid, new EventFilter()));

        Assert.Equal(0m, summary.TotalAdvanced);
        Assert.Equal(0, summary.ExpenseCount);
    }

    [Fact]
    public async Task ListAsync_TotalAdvanced_IsZero_WhenAllSharesZero()
    {
        var evt = new Event { Name = "Toàn 0đ", StartDate = Start, EndDate = End, UpdatedAt = EventUpdated };
        var expense = new Expense { Name = "Ăn tối", ExpenseTime = Start, UpdatedAt = EventUpdated };
        expense.Shares.Add(new Share { Amount = 0m, UpdatedAt = EventUpdated });
        expense.Shares.Add(new Share { Amount = 0m, UpdatedAt = EventUpdated });
        evt.Expenses.Add(expense);
        _events.StoredEvent = evt;

        var summary = Assert.Single(await CreateService().ListAsync(UserUuid, new EventFilter()));

        Assert.Equal(0m, summary.TotalAdvanced);
    }

    [Fact]
    public async Task ListAsync_MapsEffectiveUpdatedAt_FromLatestChildActivity()
    {
        _events.StoredEvent = StoredEvent(); // a share bumped to LatestChildActivity, later than the event row

        var summary = Assert.Single(await CreateService().ListAsync(UserUuid, new EventFilter()));

        Assert.Equal(LatestChildActivity, summary.UpdatedAt);
    }

    [Fact]
    public async Task ListAsync_UpdatedAt_FallsBackToEventTimestamp_WhenNoChildren()
    {
        _events.StoredEvent = new Event { Name = "Rỗng", StartDate = Start, EndDate = End, UpdatedAt = EventUpdated };

        var summary = Assert.Single(await CreateService().ListAsync(UserUuid, new EventFilter()));

        Assert.Equal(EventUpdated, summary.UpdatedAt);
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Event? StoredEvent { get; set; }

        public EventWriteResult<Event> CreateResult { get; set; } = EventWriteResult<Event>.Fail(EventWriteStatus.EventNotFound);

        public EventWriteStatus UpdateStatus { get; set; } = EventWriteStatus.Success;

        public EventWriteStatus CloseStatus { get; set; } = EventWriteStatus.Success;

        public EventWriteStatus DeleteStatus { get; set; } = EventWriteStatus.Success;

        public CreateEventData? LastCreateData { get; private set; }

        public Task<IReadOnlyList<Event>> ListByUserAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Event>>(StoredEvent is null ? [] : [StoredEvent]);

        public Task<Event?> GetByUuidAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredEvent);

        public Task<EventWriteResult<Event>> CreateAsync(string userUuid, CreateEventData data, CancellationToken cancellationToken = default)
        {
            LastCreateData = data;
            return Task.FromResult(CreateResult);
        }

        public Task<int> CountOpenByUserAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredEvent is { IsClosed: false } ? 1 : 0);

        public Task<EventWriteResult<Event>> UpdateAsync(string userUuid, string eventUuid, UpdateEventData data, CancellationToken cancellationToken = default) =>
            Task.FromResult(UpdateStatus == EventWriteStatus.Success
                ? EventWriteResult<Event>.Success(StoredEvent!)
                : EventWriteResult<Event>.Fail(UpdateStatus));

        public Task<EventWriteStatus> CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(CloseStatus);

        public Task<EventWriteStatus> DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteStatus);

        public IQueryable<Event> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
