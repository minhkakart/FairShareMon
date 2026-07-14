using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Stats;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Validators.Stats;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>StatsService</c> over a fake <see cref="IStatsRepository"/> plus the real
/// AutoMapper <see cref="StatsProfile"/> and the real validators (no DB). Proves: balance maps the repo
/// aggregates to rows with <c>Balance = Advanced - Owed</c> and the event header, an event miss → 9000,
/// an empty aggregate → empty rows; overview maps totals + echoes the range and validation
/// short-circuits the repo (from &gt; to never hits the DB); by-category resolves the owned event id,
/// maps rows, rejects both-scopes before touching the repo, and maps an event miss to 9000. Assertions
/// target stable error CODES.
/// </summary>
public class StatsServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000000e6";

    private readonly FakeStatsRepository _stats = new();

    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<StatsProfile>()).CreateMapper();

    private StatsService CreateService() =>
        new(_stats, _mapper, new StatsRangeRequestValidator(), new ByCategoryStatsRequestValidator());

    private static readonly DateTime From = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static Event OwnedEvent(bool closed = false) =>
        new() { Id = 42, Name = "Đà Lạt", IsClosed = closed };

    // ---- Balance -----------------------------------------------------------------------------------

    [Fact]
    public async Task GetEventBalanceAsync_Miss_ThrowsEventNotFound9000()
    {
        _stats.OwnedEvent = null;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetEventBalanceAsync(UserUuid, "no-such"));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task GetEventBalanceAsync_MapsRowsWithBalanceAndHeader()
    {
        var evt = OwnedEvent(closed: true);
        _stats.OwnedEvent = evt;
        _stats.BalanceAggregates =
        [
            new MemberBalanceAggregate("m-binh", "Bình", false, false, 800_000m, 500_000m),
            new MemberBalanceAggregate("m-cuong", "Cường", false, true, 0m, 500_000m)
        ];

        var response = await CreateService().GetEventBalanceAsync(UserUuid, "e-1");

        Assert.Equal(evt.Uuid, response.EventUuid);
        Assert.Equal("Đà Lạt", response.EventName);
        Assert.True(response.IsClosed);
        Assert.Equal(42UL, _stats.LastBalanceEventId); // service passed the resolved event id

        var binh = Assert.Single(response.Rows, row => row.MemberUuid == "m-binh");
        Assert.Equal(800_000m, binh.Advanced);
        Assert.Equal(500_000m, binh.Owed);
        Assert.Equal(300_000m, binh.Balance); // Advanced - Owed (OQ1/OQ14)

        var cuong = Assert.Single(response.Rows, row => row.MemberUuid == "m-cuong");
        Assert.Equal(-500_000m, cuong.Balance);
        Assert.True(cuong.IsDeleted); // denormalized deleted flag preserved (§4.7)
    }

    [Fact]
    public async Task GetEventBalanceAsync_EmptyAggregate_ReturnsEmptyRows()
    {
        _stats.OwnedEvent = OwnedEvent();
        _stats.BalanceAggregates = [];

        var response = await CreateService().GetEventBalanceAsync(UserUuid, "e-1");

        Assert.Empty(response.Rows); // owned-but-empty event → empty rows (OQ15)
    }

    // ---- Overview ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetOverviewAsync_MapsTotalsAndEchoesRange()
    {
        _stats.Overview = new OverviewAggregate(750_000m, 3);

        var response = await CreateService().GetOverviewAsync(UserUuid, new StatsRangeRequest { From = From, To = To });

        Assert.Equal(750_000m, response.TotalSpending);
        Assert.Equal(3, response.ExpenseCount);
        Assert.Equal(From, response.From); // echoed
        Assert.Equal(To, response.To);
    }

    [Fact]
    public async Task GetOverviewAsync_OmittedBounds_PassesNullsToRepoAndEchoesNulls()
    {
        _stats.Overview = new OverviewAggregate(0m, 0);

        var response = await CreateService().GetOverviewAsync(UserUuid, new StatsRangeRequest());

        Assert.Null(response.From);
        Assert.Null(response.To);
        Assert.True(_stats.GetOverviewCalled);
        Assert.Null(_stats.LastOverviewFrom);
        Assert.Null(_stats.LastOverviewTo);
    }

    [Fact]
    public async Task GetOverviewAsync_FromAfterTo_ThrowsValidationAndSkipsRepo()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().GetOverviewAsync(UserUuid, new StatsRangeRequest { From = To, To = From }));

        Assert.False(_stats.GetOverviewCalled); // validation short-circuits the repo
    }

    // ---- By-category -------------------------------------------------------------------------------

    [Fact]
    public async Task GetByCategoryAsync_TimeRange_MapsRowsAndEchoesRange()
    {
        _stats.CategoryAggregates =
        [
            new CategoryStatAggregate("c-food", "Ăn uống", "#F97316", "food", false, 500_000m, 4),
            new CategoryStatAggregate("c-travel", "Di chuyển", "#3B82F6", null, true, 200_000m, 1)
        ];

        var response = await CreateService().GetByCategoryAsync(UserUuid, new ByCategoryStatsRequest { From = From, To = To });

        Assert.Null(response.EventUuid);
        Assert.Equal(From, response.From);
        Assert.Equal(To, response.To);
        Assert.Null(_stats.LastByCategoryEventId); // time-range mode → no event id

        var travel = Assert.Single(response.Rows, row => row.CategoryUuid == "c-travel");
        Assert.Equal(200_000m, travel.Total);
        Assert.Equal(1, travel.ExpenseCount);
        Assert.True(travel.IsDeleted); // deleted category with history still rendered (§4.7)
        Assert.Null(travel.Icon);
    }

    [Fact]
    public async Task GetByCategoryAsync_EventMode_ResolvesOwnedEventIdAndClearsRange()
    {
        _stats.OwnedEvent = OwnedEvent();
        _stats.CategoryAggregates = [];

        var response = await CreateService().GetByCategoryAsync(UserUuid, new ByCategoryStatsRequest { EventUuid = "e-1" });

        Assert.Equal("e-1", response.EventUuid);
        Assert.Null(response.From); // range nulled in event mode
        Assert.Null(response.To);
        Assert.Equal(42UL, _stats.LastByCategoryEventId); // owned event id passed to the repo
    }

    [Fact]
    public async Task GetByCategoryAsync_EventMiss_ThrowsEventNotFound9000()
    {
        _stats.OwnedEvent = null;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetByCategoryAsync(UserUuid, new ByCategoryStatsRequest { EventUuid = "no-such" }));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
        Assert.False(_stats.GetByCategoryCalled); // never reached the aggregation
    }

    [Fact]
    public async Task GetByCategoryAsync_BothScopes_ThrowsValidationAndSkipsRepo()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().GetByCategoryAsync(UserUuid, new ByCategoryStatsRequest { EventUuid = "e-1", From = From }));

        Assert.False(_stats.FindOwnedEventCalled); // validation runs before the ownership resolve
        Assert.False(_stats.GetByCategoryCalled);
    }

    private sealed class FakeStatsRepository : IStatsRepository
    {
        public Event? OwnedEvent { get; set; }

        public IReadOnlyList<MemberBalanceAggregate> BalanceAggregates { get; set; } = [];

        public OverviewAggregate Overview { get; set; } = new(0m, 0);

        public IReadOnlyList<CategoryStatAggregate> CategoryAggregates { get; set; } = [];

        public ulong? LastBalanceEventId { get; private set; }

        public bool GetOverviewCalled { get; private set; }

        public DateTime? LastOverviewFrom { get; private set; }

        public DateTime? LastOverviewTo { get; private set; }

        public bool FindOwnedEventCalled { get; private set; }

        public bool GetByCategoryCalled { get; private set; }

        public ulong? LastByCategoryEventId { get; private set; }

        public Task<Event?> FindOwnedEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            FindOwnedEventCalled = true;
            return Task.FromResult(OwnedEvent);
        }

        public Task<IReadOnlyList<MemberBalanceAggregate>> GetEventBalanceAsync(string userUuid, ulong eventId, CancellationToken cancellationToken = default)
        {
            LastBalanceEventId = eventId;
            return Task.FromResult(BalanceAggregates);
        }

        public Task<OverviewAggregate> GetOverviewAsync(string userUuid, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            GetOverviewCalled = true;
            LastOverviewFrom = from;
            LastOverviewTo = to;
            return Task.FromResult(Overview);
        }

        public Task<IReadOnlyList<CategoryStatAggregate>> GetByCategoryAsync(string userUuid, DateTime? from, DateTime? to, ulong? eventId, CancellationToken cancellationToken = default)
        {
            GetByCategoryCalled = true;
            LastByCategoryEventId = eventId;
            return Task.FromResult(CategoryAggregates);
        }

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
