using System.Text.RegularExpressions;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Export;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Models.Tags;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Export;
using FairShareMonApi.Services.Api.Stats;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="ExportService"/> over fake M5/M7 application services plus a
/// capturing <see cref="IExportFormatter"/> that records the neutral <see cref="ExportDocument"/> the
/// service builds (M8, OQ10/OQ11/OQ12/OQ13/OQ14/OQ15/OQ18). Proves: format resolution (default/csv/CSV →
/// the CSV formatter, an unknown format → <c>ValidationFailed</c> 1001); the expense document (header
/// block with +7 instant + <c>0.00</c> money, per-member table incl. owner-rep 0đ and a soft-deleted
/// member "(đã xóa)", a <c>Tổng cộng</c> row equal to the total); the event document (header with the
/// calendar range no-shift, Section 1 per-member owed + merged notes, Section 2 the M7 balance with a
/// sum-to-zero <c>Tổng cộng</c>); merged-notes join with the expense-name prefix; that a resource-owned
/// miss thrown by the underlying service propagates; and the filename shape/slug (OQ18). Assertions
/// target stable error CODES.
/// </summary>
public class ExportServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000000e6";
    private const string ExpenseUuid = "0198a5c2-1111-7000-8000-000000000001";
    private const string EventUuid = "0198a5c2-2222-7000-8000-000000000002";

    private static readonly DateTime StartBoundary = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndBoundary = new DateTime(2026, 3, 3, 23, 59, 59, DateTimeKind.Utc).AddTicks(9_999_990);
    private static readonly DateTime ExpenseTime = new(2026, 3, 1, 18, 30, 0, DateTimeKind.Utc);

    private readonly FakeExpensesService _expenses = new();
    private readonly FakeStatsService _stats = new();
    private readonly FakeEventsService _events = new();
    private readonly CapturingCsvFormatter _formatter = new();

    private ExportService CreateService() => new(_expenses, _stats, _events, [_formatter]);

    // ---- Format resolution -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("csv")]
    [InlineData("CSV")]
    [InlineData("  csv  ")]
    public async Task ExportExpenseAsync_DefaultOrCsv_UsesCsvFormatter(string? format)
    {
        _expenses.Expenses[ExpenseUuid] = SampleExpense();

        var file = await CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, format);

        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.EndsWith(".csv", file.FileName);
        Assert.NotNull(_formatter.Last);
    }

    [Fact]
    public async Task ExportExpenseAsync_UnsupportedFormat_ThrowsValidationFailed()
    {
        _expenses.Expenses[ExpenseUuid] = SampleExpense();

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, "xlsx"));

        Assert.Equal(ErrorCodes.ValidationFailed, exception.Code);
        Assert.Null(_formatter.Last); // failed before building/rendering a document
    }

    [Fact]
    public async Task ExportEventAsync_UnsupportedFormat_ThrowsValidationFailed()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent();

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().ExportEventAsync(UserUuid, EventUuid, "json"));

        Assert.Equal(ErrorCodes.ValidationFailed, exception.Code);
    }

    // ---- Expense document --------------------------------------------------------------------------

    [Fact]
    public async Task ExportExpenseAsync_BuildsHeaderBlock_WithPlus7InstantAndMoney()
    {
        _expenses.Expenses[ExpenseUuid] = SampleExpense();

        await CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, null);

        var header = HeaderFields(_formatter.Last!, sectionIndex: 0);
        Assert.Equal("Ăn tối Đà Lạt", header["Tên phiếu"]);
        Assert.Equal("02/03/2026 01:30", header["Thời điểm chi"]); // +7 rolled forward (OQ6b)
        Assert.Equal("An", header["Người trả"]);
        Assert.Equal("Ăn uống", header["Danh mục"]);
        Assert.Equal("Đà Lạt", header["Đợt"]);
        Assert.Equal("Không", header["Đã trả"]);
        Assert.Equal("800000.00", header["Tổng tiền"]); // invariant 0.00 (OQ5a)
    }

    [Fact]
    public async Task ExportExpenseAsync_NoEvent_ShowsNoEventLabel()
    {
        var expense = SampleExpense();
        expense.EventUuid = null;
        expense.EventName = null;
        _expenses.Expenses[ExpenseUuid] = expense;

        await CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, null);

        Assert.Equal("(không thuộc đợt)", HeaderFields(_formatter.Last!, 0)["Đợt"]);
    }

    [Fact]
    public async Task ExportExpenseAsync_ShareTable_IncludesEveryMemberSortedWithDeletedSuffixAndTotalRow()
    {
        _expenses.Expenses[ExpenseUuid] = SampleExpense();

        await CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, null);

        var table = _formatter.Last!.Sections[1];
        Assert.Equal(new[] { "Thành viên", "Số tiền gánh", "Ghi chú" }, table.ColumnHeaders);

        // Rows sorted amount desc then name; last row is the total. Owner-rep 0đ present; deleted suffix.
        Assert.Equal(new[] { "Bình", "500000.00", "chia đều" }, table.Rows[0]);
        Assert.Equal(new[] { "Cường (đã xóa)", "300000.00", "" }, table.Rows[1]);
        Assert.Equal(new[] { "An", "0.00", "" }, table.Rows[2]); // owner-rep at 0đ still appears (§4.7)
        Assert.Equal(new[] { "Tổng cộng", "800000.00", "" }, table.Rows[^1]);
    }

    [Fact]
    public async Task ExportExpenseAsync_Miss_PropagatesExpenseNotFound()
    {
        _expenses.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    [Fact]
    public async Task ExportExpenseAsync_FilenameShape_ExpenseUuidAndDate()
    {
        _expenses.Expenses[ExpenseUuid] = SampleExpense();

        var file = await CreateService().ExportExpenseAsync(UserUuid, ExpenseUuid, null);

        Assert.Matches($"^expense-{Regex.Escape(ExpenseUuid)}-\\d{{8}}\\.csv$", file.FileName);
    }

    // ---- Event document ----------------------------------------------------------------------------

    [Fact]
    public async Task ExportEventAsync_BuildsHeader_WithCalendarRangeNoShiftAndStatus()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent(closed: false);

        await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        var header = HeaderFields(_formatter.Last!, 0);
        Assert.Equal("Đà Lạt", header["Tên đợt"]);
        // End boundary 23:59:59.999999Z keeps its own calendar day 03/03 - no +7 roll to 04/03 (OQ6b).
        Assert.Equal("01/03/2026 - 03/03/2026", header["Khoảng thời gian"]);
        Assert.Equal("Đang mở", header["Trạng thái"]);
    }

    [Fact]
    public async Task ExportEventAsync_ClosedEvent_ShowsClosedStatus()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent(closed: true);

        await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        Assert.Equal("Đã chốt", HeaderFields(_formatter.Last!, 0)["Trạng thái"]);
    }

    [Fact]
    public async Task ExportEventAsync_Section1_PerMemberOwedAndMergedNotes()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent();
        SeedMergedNotesSource();

        await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        var summary = _formatter.Last!.Sections[1];
        Assert.Equal("Tổng hợp phần gánh theo thành viên", summary.Name);
        Assert.Equal(new[] { "Thành viên", "Tổng phần gánh", "Ghi chú gộp" }, summary.ColumnHeaders);

        var binh = Assert.Single(summary.Rows, row => row[0] == "Bình");
        Assert.Equal("500000.00", binh[1]); // Owed from the M7 balance (OQ15)
        // Notes merged across the event's expenses, each prefixed by the expense name (OQ13).
        Assert.Equal("Ăn tối: chia đều; Taxi: về sân bay", binh[2]);
    }

    [Fact]
    public async Task ExportEventAsync_Section2_BalanceTableSumsToZero()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent();

        await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        var balance = _formatter.Last!.Sections[2];
        Assert.Equal("Cân bằng nợ", balance.Name);
        Assert.Equal(new[] { "Thành viên", "Đã ứng", "Phải gánh", "Cân bằng" }, balance.ColumnHeaders);

        var total = balance.Rows[^1];
        Assert.Equal("Tổng cộng", total[0]);
        Assert.Equal("0.00", total[3]); // sum-to-zero Cân bằng (§3.7)
    }

    [Fact]
    public async Task ExportEventAsync_DeletedMember_ShowsDeletedSuffixInBothSections()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent();

        await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        var section1 = _formatter.Last!.Sections[1];
        var section2 = _formatter.Last!.Sections[2];
        Assert.Contains(section1.Rows, row => row[0] == "Cường (đã xóa)");
        Assert.Contains(section2.Rows, row => row[0] == "Cường (đã xóa)");
    }

    [Fact]
    public async Task ExportEventAsync_Miss_PropagatesEventNotFound()
    {
        _stats.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().ExportEventAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task ExportEventAsync_FilenameSlug_DiacriticsFolded()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent();
        _events.Event.Name = "Chuyến đi Đà Lạt!";

        var file = await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        Assert.Matches("^event-chuyen-di-da-lat-\\d{8}\\.csv$", file.FileName);
    }

    [Fact]
    public async Task ExportEventAsync_EmptyName_FallsBackToUuidInFilename()
    {
        _stats.Balance = SampleBalance();
        _events.Event = SampleEvent();
        _events.Event.Name = "   ";

        var file = await CreateService().ExportEventAsync(UserUuid, EventUuid, null);

        Assert.Matches($"^event-{Regex.Escape(EventUuid)}-\\d{{8}}\\.csv$", file.FileName);
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static MemberResponse Member(string uuid, string name, bool ownerRep = false, bool deleted = false) =>
        new() { Uuid = uuid, Name = name, IsOwnerRepresentative = ownerRep, IsDeleted = deleted };

    private static ExpenseResponse SampleExpense()
    {
        var an = Member("m-an", "An", ownerRep: true);
        var binh = Member("m-binh", "Bình");
        var cuong = Member("m-cuong", "Cường", deleted: true);

        return new ExpenseResponse
        {
            Uuid = ExpenseUuid,
            Name = "Ăn tối Đà Lạt",
            Description = "Bữa tối",
            ExpenseTime = ExpenseTime,
            Total = 800_000m,
            Category = new CategoryResponse { Uuid = "c-food", Name = "Ăn uống" },
            Payer = an,
            IsSettled = false,
            EventUuid = EventUuid,
            EventName = "Đà Lạt",
            Tags = new List<TagResponse> { new() { Uuid = "t-1", Name = "du lịch" } },
            Shares =
            [
                new ShareResponse { Uuid = "s-an", Member = an, Amount = 0m },
                new ShareResponse { Uuid = "s-binh", Member = binh, Amount = 500_000m, Note = "chia đều" },
                new ShareResponse { Uuid = "s-cuong", Member = cuong, Amount = 300_000m }
            ]
        };
    }

    private static EventResponse SampleEvent(bool closed = false) => new()
    {
        Uuid = EventUuid,
        Name = "Đà Lạt",
        StartDate = StartBoundary,
        EndDate = EndBoundary,
        IsClosed = closed
    };

    private static EventBalanceResponse SampleBalance() => new()
    {
        EventUuid = EventUuid,
        EventName = "Đà Lạt",
        Rows =
        [
            new MemberBalanceRow { MemberUuid = "m-an", MemberName = "An", IsOwnerRepresentative = true, Advanced = 0m, Owed = 0m, Balance = 0m },
            new MemberBalanceRow { MemberUuid = "m-binh", MemberName = "Bình", Advanced = 800_000m, Owed = 500_000m, Balance = 300_000m },
            new MemberBalanceRow { MemberUuid = "m-cuong", MemberName = "Cường", IsDeleted = true, Advanced = 0m, Owed = 500_000m, Balance = -300_000m }
        ]
    };

    /// <summary>Seeds two expenses in the event so Bình's notes merge across them (OQ13).</summary>
    private void SeedMergedNotesSource()
    {
        var binh = Member("m-binh", "Bình");

        _expenses.Summaries.Add(new ExpenseSummaryResponse { Uuid = "ex-1", Name = "Ăn tối" });
        _expenses.Summaries.Add(new ExpenseSummaryResponse { Uuid = "ex-2", Name = "Taxi" });

        _expenses.Expenses["ex-1"] = new ExpenseResponse
        {
            Uuid = "ex-1", Name = "Ăn tối",
            Shares = [new ShareResponse { Member = binh, Amount = 300_000m, Note = "chia đều" }]
        };
        _expenses.Expenses["ex-2"] = new ExpenseResponse
        {
            Uuid = "ex-2", Name = "Taxi",
            Shares = [new ShareResponse { Member = binh, Amount = 200_000m, Note = "về sân bay" }]
        };
    }

    private static IReadOnlyDictionary<string, string> HeaderFields(ExportDocument document, int sectionIndex) =>
        document.Sections[sectionIndex].HeaderFields!.ToDictionary(field => field.Key, field => field.Value);

    // ---- Fakes -------------------------------------------------------------------------------------

    private sealed class FakeExpensesService : IExpensesService
    {
        public Dictionary<string, ExpenseResponse> Expenses { get; } = new();

        public List<ExpenseSummaryResponse> Summaries { get; } = new();

        public bool ThrowNotFound { get; set; }

        public Task<ExpenseResponse> GetAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFound || !Expenses.TryGetValue(expenseUuid, out var expense))
                throw new ErrorException(ErrorCodes.ExpenseNotFound, "Không tìm thấy phiếu chi tiêu.");
            return Task.FromResult(expense);
        }

        public Task<IReadOnlyList<ExpenseSummaryResponse>> ListAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<ExpenseSummaryResponse>)Summaries);

        public Task<ExpenseResponse> CreateAsync(string userUuid, CreateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> UpdateAsync(string userUuid, string expenseUuid, UpdateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSettledAsync(string userUuid, string expenseUuid, SetSettledRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> AssignEventAsync(string userUuid, string expenseUuid, AssignEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditLogResponse>> GetHistoryAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStatsService : IStatsService
    {
        public EventBalanceResponse? Balance { get; set; }

        public bool ThrowNotFound { get; set; }

        public Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFound || Balance is null)
                throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
            return Task.FromResult(Balance);
        }

        public Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeEventsService : IEventsService
    {
        public EventResponse? Event { get; set; }

        public Task<EventResponse> GetAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
            Event is null
                ? throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.")
                : Task.FromResult(Event);

        public Task<IReadOnlyList<EventSummaryResponse>> ListAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> CreateAsync(string userUuid, CreateEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> UpdateAsync(string userUuid, string eventUuid, UpdateEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingCsvFormatter : IExportFormatter
    {
        public ExportDocument? Last { get; private set; }

        public ExportFormat Format => ExportFormat.Csv;

        public string ContentType => "text/csv; charset=utf-8";

        public string FileExtension => "csv";

        public byte[] Render(ExportDocument document)
        {
            Last = document;
            return [1, 2, 3];
        }
    }
}
