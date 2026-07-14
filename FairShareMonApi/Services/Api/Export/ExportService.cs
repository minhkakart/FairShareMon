using System.Globalization;
using System.Text;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Export;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Services.Api.Export;

/// <summary>
/// Dịch vụ xuất phiếu (§3.5) và đợt (§3.6) ra tệp tải về (M8). Chỉ đọc và định dạng lại dữ liệu M5
/// (phiếu/phần gánh) và M7 (cân bằng nợ) - không thêm bảng, không đổi dữ liệu. Dùng lại các application
/// service (OQ11) nên hưởng sẵn 404 resource-owned (<c>ExpenseNotFound</c> 6000 / <c>EventNotFound</c>
/// 9000), tổng tiền suy ra và cân bằng M7. Dựng <see cref="ExportDocument"/> độc lập định dạng (mọi số
/// tiền/ngày giờ chuyển chuỗi một lần qua <see cref="ExportValueFormatter"/>) rồi chọn
/// <see cref="IExportFormatter"/> theo <c>format</c> để kết xuất; định dạng không hỗ trợ -&gt; 400
/// <c>ValidationFailed</c> (1001, OQ10).
/// </summary>
public interface IExportService
{
    Task<ExportedFile> ExportExpenseAsync(string userUuid, string expenseUuid, string? format, CancellationToken cancellationToken = default);

    Task<ExportedFile> ExportEventAsync(string userUuid, string eventUuid, string? format, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IExportService))]
public sealed class ExportService(
    IExpensesService expensesService,
    IStatsService statsService,
    IEventsService eventsService,
    IEnumerable<IExportFormatter> formatters) : IExportService
{
    private const string DeletedSuffix = " (đã xóa)";
    private const string NoEventLabel = "(không thuộc đợt)";

    public async Task<ExportedFile> ExportExpenseAsync(string userUuid, string expenseUuid, string? format, CancellationToken cancellationToken = default)
    {
        var formatter = ResolveFormatter(format);

        var expense = await expensesService.GetAsync(userUuid, expenseUuid, cancellationToken);

        var document = BuildExpenseDocument(expense);
        var bytes = formatter.Render(document);
        var fileName = $"expense-{expense.Uuid}-{TodayStamp()}.{formatter.FileExtension}";

        return new ExportedFile(bytes, formatter.ContentType, fileName);
    }

    public async Task<ExportedFile> ExportEventAsync(string userUuid, string eventUuid, string? format, CancellationToken cancellationToken = default)
    {
        var formatter = ResolveFormatter(format);

        // Cân bằng M7 (throw EventNotFound 9000 khi không sở hữu) + thông tin đợt cho khoảng thời gian.
        var balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, cancellationToken);
        var evt = await eventsService.GetAsync(userUuid, eventUuid, cancellationToken);
        var mergedNotes = await GatherMergedNotesAsync(userUuid, eventUuid, cancellationToken);

        var document = BuildEventDocument(evt, balance, mergedNotes);
        var bytes = formatter.Render(document);
        var slug = Slugify(evt.Name, evt.Uuid);
        var fileName = $"event-{slug}-{TodayStamp()}.{formatter.FileExtension}";

        return new ExportedFile(bytes, formatter.ContentType, fileName);
    }

    private IExportFormatter ResolveFormatter(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();

        var target = normalized switch
        {
            "csv" => ExportFormat.Csv,
            _ => throw UnsupportedFormat()
        };

        // M10 Premium feature-gate hook (OQ5/§3.11 "mở rộng"): CSV stays Free. When a non-CSV
        // ExportFormat is added, gate that branch with ITierService.EnsurePremiumFeature(...) here
        // (inject ITierService into this service at that point) before returning the formatter.
        return formatters.FirstOrDefault(f => f.Format == target) ?? throw UnsupportedFormat();
    }

    private static ExportDocument BuildExpenseDocument(ExpenseResponse expense)
    {
        var headerFields = new List<KeyValuePair<string, string>>
        {
            new("Tên phiếu", expense.Name),
            new("Mô tả", expense.Description ?? string.Empty),
            new("Thời điểm chi", ExportValueFormatter.FormatInstant(expense.ExpenseTime)),
            new("Người trả", DisplayMember(expense.Payer)),
            new("Danh mục", DisplayCategory(expense.Category)),
            new("Nhãn", string.Join(", ", expense.Tags.Select(t => t.Name))),
            new("Đợt", expense.EventName ?? NoEventLabel),
            new("Đã trả", expense.IsSettled ? "Có" : "Không"),
            new("Tổng tiền", ExportValueFormatter.FormatMoney(expense.Total))
        };

        // Bảng phần gánh: sắp xếp theo số tiền giảm dần rồi tên tăng dần (OQ12).
        var shareRows = expense.Shares
            .OrderByDescending(s => s.Amount)
            .ThenBy(s => s.Member.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => (IReadOnlyList<string>)new[]
            {
                DisplayMember(s.Member),
                ExportValueFormatter.FormatMoney(s.Amount),
                s.Note ?? string.Empty
            })
            .ToList();

        shareRows.Add(new[] { "Tổng cộng", ExportValueFormatter.FormatMoney(expense.Total), string.Empty });

        return new ExportDocument
        {
            Title = $"Phiếu chi tiêu: {expense.Name}",
            Sections =
            [
                new ExportSection { HeaderFields = headerFields },
                new ExportSection
                {
                    Name = "Phần gánh theo thành viên",
                    ColumnHeaders = ["Thành viên", "Số tiền gánh", "Ghi chú"],
                    Rows = shareRows
                }
            ]
        };
    }

    private static ExportDocument BuildEventDocument(
        EventResponse evt,
        EventBalanceResponse balance,
        IReadOnlyDictionary<string, string> mergedNotes)
    {
        var headerFields = new List<KeyValuePair<string, string>>
        {
            new("Tên đợt", evt.Name),
            new("Khoảng thời gian",
                $"{ExportValueFormatter.FormatCalendarDate(evt.StartDate)} - {ExportValueFormatter.FormatCalendarDate(evt.EndDate)}"),
            new("Trạng thái", evt.IsClosed ? "Đã chốt" : "Đang mở")
        };

        // Phần 1: tổng hợp phần gánh theo thành viên (Owed từ M7 + ghi chú gộp) - OQ14/OQ15/OQ13.
        var summaryRows = balance.Rows
            .Select(r => (IReadOnlyList<string>)new[]
            {
                DisplayMember(r),
                ExportValueFormatter.FormatMoney(r.Owed),
                mergedNotes.TryGetValue(r.MemberUuid, out var note) ? note : string.Empty
            })
            .ToList();

        // Phần 2: bảng cân bằng nợ M7 nguyên vẹn + dòng Tổng cộng (Cân bằng = 0) - OQ14.
        var balanceRows = balance.Rows
            .Select(r => (IReadOnlyList<string>)new[]
            {
                DisplayMember(r),
                ExportValueFormatter.FormatMoney(r.Advanced),
                ExportValueFormatter.FormatMoney(r.Owed),
                ExportValueFormatter.FormatMoney(r.Balance)
            })
            .ToList();

        balanceRows.Add(new[]
        {
            "Tổng cộng",
            ExportValueFormatter.FormatMoney(balance.Rows.Sum(r => r.Advanced)),
            ExportValueFormatter.FormatMoney(balance.Rows.Sum(r => r.Owed)),
            ExportValueFormatter.FormatMoney(balance.Rows.Sum(r => r.Balance))
        });

        return new ExportDocument
        {
            Title = $"Đợt chi tiêu: {evt.Name}",
            Sections =
            [
                new ExportSection { HeaderFields = headerFields },
                new ExportSection
                {
                    Name = "Tổng hợp phần gánh theo thành viên",
                    ColumnHeaders = ["Thành viên", "Tổng phần gánh", "Ghi chú gộp"],
                    Rows = summaryRows
                },
                new ExportSection
                {
                    Name = "Cân bằng nợ",
                    ColumnHeaders = ["Thành viên", "Đã ứng", "Phải gánh", "Cân bằng"],
                    Rows = balanceRows
                }
            ]
        };
    }

    /// <summary>
    /// Gộp ghi chú các phần gánh của mỗi thành viên trên toàn đợt (OQ13/OQ15): với mỗi thành viên, nối
    /// các ghi chú khác rỗng bằng "; ", mỗi ghi chú kèm tiền tố tên phiếu. Dùng lại các application
    /// service (danh sách phiếu của đợt + chi tiết từng phiếu để lấy ghi chú phần gánh).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> GatherMergedNotesAsync(
        string userUuid, string eventUuid, CancellationToken cancellationToken)
    {
        var summaries = await expensesService.ListAsync(
            userUuid, new ExpenseFilter { EventUuid = eventUuid }, cancellationToken);

        var fragmentsByMember = new Dictionary<string, List<string>>();

        foreach (var summary in summaries)
        {
            var expense = await expensesService.GetAsync(userUuid, summary.Uuid, cancellationToken);

            foreach (var share in expense.Shares)
            {
                if (string.IsNullOrWhiteSpace(share.Note))
                    continue;

                if (!fragmentsByMember.TryGetValue(share.Member.Uuid, out var fragments))
                {
                    fragments = [];
                    fragmentsByMember[share.Member.Uuid] = fragments;
                }

                fragments.Add($"{expense.Name}: {share.Note}");
            }
        }

        return fragmentsByMember.ToDictionary(
            pair => pair.Key,
            pair => string.Join("; ", pair.Value));
    }

    private static string DisplayMember(MemberResponse member) =>
        member.IsDeleted ? member.Name + DeletedSuffix : member.Name;

    private static string DisplayMember(MemberBalanceRow row) =>
        row.IsDeleted ? row.MemberName + DeletedSuffix : row.MemberName;

    private static string DisplayCategory(CategoryResponse category) =>
        category.IsDeleted ? category.Name + DeletedSuffix : category.Name;

    private static string TodayStamp() =>
        AppDateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Tạo slug ASCII từ tên đợt cho tên tệp (OQ18): bỏ dấu, chữ thường, gom các ký tự ngoài
    /// <c>[a-z0-9]</c> thành "-", cắt tối đa 40 ký tự; rỗng thì trả về <paramref name="fallback"/> (uuid).
    /// </summary>
    private static string Slugify(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var withoutMarks = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                withoutMarks.Append(ch);
        }

        var lowered = withoutMarks.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Replace('đ', 'd');

        var slug = new StringBuilder(lowered.Length);
        var lastDash = false;
        foreach (var ch in lowered)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                slug.Append('-');
                lastDash = true;
            }
        }

        var result = slug.ToString().Trim('-');
        if (result.Length > 40)
            result = result[..40].Trim('-');

        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    private static ErrorException UnsupportedFormat() =>
        new(ErrorCodes.ValidationFailed, "Định dạng xuất không được hỗ trợ.");
}
