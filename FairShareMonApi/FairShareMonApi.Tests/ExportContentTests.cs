using System.Net;
using System.Net.Http.Json;
using System.Text;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) that seed a ledger over the M3-M7 HTTP endpoints, then
/// GET the M8 export endpoint and decode the CSV bytes to assert the rendered CONTENT (M8,
/// OQ5/OQ6/OQ12/OQ13/OQ14/OQ15). Expense: the per-member share rows (incl. owner-rep 0đ and a
/// soft-deleted member/category shown "(đã xóa)"), the total row, and the +7 <c>expense_time</c>. Event:
/// the calendar date range with NO day-roll on the 23:59:59.999999Z end boundary, Section 1 per-member
/// owed + merged notes, and Section 2 the M7 balance matching <c>GetEventBalanceAsync</c> with a
/// sum-to-zero <c>Tổng cộng</c>. Exercises the full stack against the real DB.
/// </summary>
[Collection("AuthIntegration")]
public class ExportContentTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Evening1830 = new(2026, 7, 15, 18, 30, 0, DateTimeKind.Utc);

    private static async Task<string> GetCsvTextAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var preamble = Encoding.UTF8.GetPreamble();
        return Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
    }

    private static string[] Lines(string csv) =>
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static async Task<string> CreateCategoryAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name, color = "#F97316" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
    }

    // ---- Expense export content --------------------------------------------------------------------

    [SkippableFact]
    public async Task ExportExpense_Content_ShowsSharesTotalDeletedNamesAndPlus7Time()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var cuong = await CreateMemberAsync(client, "Cường");
        var customCategory = await CreateCategoryAsync(client, "Ăn uống nội bộ");

        var expense = Uuid(await CreateExpenseAsync(client, new
        {
            name = "Ăn tối Đà Lạt",
            expenseTime = Evening1830,
            payerMemberUuid = an,
            categoryUuid = customCategory,
            shares = new[]
            {
                new { memberUuid = an, amount = 0m, note = (string?)null },
                new { memberUuid = binh, amount = 500_000m, note = (string?)"chia đều" },
                new { memberUuid = cuong, amount = 300_000m, note = (string?)null }
            }
        }));

        // Soft-delete a share member and the category AFTER the expense references them (§4.7 history).
        await DeleteMemberAsync(client, binh);
        await DeleteCategoryAsync(client, customCategory);

        var csv = await GetCsvTextAsync(client, $"api/v1/expenses/{expense}/export");

        Assert.Contains("Thời điểm chi,16/07/2026 01:30", csv); // 18:30Z + 7h rolls to next day (OQ6b)
        Assert.Contains("Danh mục,Ăn uống nội bộ (đã xóa)", csv); // deleted category name kept (§4.7)
        Assert.Contains("Bình (đã xóa),500000.00,chia đều", csv); // deleted member row + note (§4.7/OQ5a)
        Assert.Contains("Cường,300000.00,", csv);
        Assert.Contains("Tôi,0.00,", csv); // owner-rep 0đ share present (default name "Tôi")
        Assert.Contains("Tổng cộng,800000.00,", csv); // derived total row
    }

    // ---- Event export content ----------------------------------------------------------------------

    [SkippableFact]
    public async Task ExportEvent_Content_CalendarRangeSummaryAndBalanceSumZero()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var cuong = await CreateMemberAsync(client, "Cường");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        // Bình advances 800k (Ăn tối), An advances 700k (Khách sạn): An +200k, Bình +300k, Cường -500k.
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 300_000m, note = (string?)null },
                new { memberUuid = binh, amount = 200_000m, note = (string?)"chia đều" },
                new { memberUuid = cuong, amount = 300_000m, note = (string?)null }
            }
        });
        await CreateExpenseAsync(client, new
        {
            name = "Khách sạn",
            expenseTime = Day15Noon,
            payerMemberUuid = an,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 200_000m },
                new { memberUuid = binh, amount = 300_000m },
                new { memberUuid = cuong, amount = 200_000m }
            }
        });

        await DeleteMemberAsync(client, cuong); // deleted member still appears in the historical report

        var csv = await GetCsvTextAsync(client, $"api/v1/events/{evt}/export");

        // Calendar range: the end boundary 23:59:59.999999Z of 16/07 shows its OWN day, not 17/07 (OQ6b).
        Assert.Contains("Khoảng thời gian,14/07/2026 - 16/07/2026", csv);
        Assert.Contains("Trạng thái,Đang mở", csv);

        // Section 1: per-member owed (from the M7 balance) + merged notes prefixed by expense name (OQ13).
        Assert.Contains("Tổng hợp phần gánh theo thành viên", csv);
        Assert.Contains("Bình,500000.00,Ăn tối: chia đều", csv);
        Assert.Contains("Cường (đã xóa)", csv); // §4.7 deleted-member display

        // Section 2: the balance table + a sum-to-zero Tổng cộng.
        Assert.Contains("Cân bằng nợ", csv);
        Assert.Contains("Bình,800000.00,500000.00,300000.00", csv); // advanced,owed,balance for Bình
        Assert.Contains("Cường (đã xóa),0.00,500000.00,-500000.00", csv);

        var balanceTotal = Lines(csv).Single(line =>
            line.StartsWith("Tổng cộng,") && line.EndsWith(",0.00"));
        Assert.EndsWith(",0.00", balanceTotal); // Cân bằng column sums to zero (§3.7)
    }

    [SkippableFact]
    public async Task ExportEvent_ClosedEvent_ContentStatusClosed()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 200_000m },
                new { memberUuid = binh, amount = 300_000m }
            }
        });
        await CloseEventAsync(client, evt);

        var csv = await GetCsvTextAsync(client, $"api/v1/events/{evt}/export");

        Assert.Contains("Trạng thái,Đã chốt", csv); // closed event still exports (OQ16)
        var balanceTotal = Lines(csv).Single(line => line.StartsWith("Tổng cộng,") && line.EndsWith(",0.00"));
        Assert.EndsWith(",0.00", balanceTotal);
    }

    // ---- CSV formula-injection hardening (end-to-end) ----------------------------------------------

    [SkippableFact]
    public async Task ExportExpense_FormulaPayloadInNameAndNote_IsNeutralizedWhileMoneyRaw()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client);
        // A member whose name is a spreadsheet-formula payload, plus a formula-leading share note.
        var evil = await CreateMemberAsync(client, "=cmd");

        var expense = Uuid(await CreateExpenseAsync(client, new
        {
            name = "Ăn tối Đà Lạt",
            expenseTime = Evening1830,
            payerMemberUuid = an,
            shares = new[]
            {
                new { memberUuid = an, amount = 0m, note = (string?)null },
                new { memberUuid = evil, amount = 500_000m, note = (string?)"=HYPERLINK(http://x)" }
            }
        }));

        var csv = await GetCsvTextAsync(client, $"api/v1/expenses/{expense}/export");

        // Both the formula member name and the formula note are neutralized with a leading single-quote,
        // while the money amount in the same row stays a raw invariant decimal.
        Assert.Contains("'=cmd,500000.00,'=HYPERLINK(http://x)", csv);
        Assert.DoesNotContain(Lines(csv), line => line.StartsWith('=')); // no un-guarded formula line
        Assert.Contains("Tổng cộng,500000.00,", csv); // derived total unaffected
    }

    [SkippableFact]
    public async Task ExportEvent_FormulaMemberName_IsNeutralizedWhileBalanceMoneyRaw()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await OwnerRepUuidAsync(client); // owner-rep "Tôi"
        var evil = await CreateMemberAsync(client, "=evil"); // formula-payload member name
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);

        // =evil advances 500k for a 500k expense split An 300k / =evil 200k → =evil +300k, An -300k.
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = evil,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 300_000m },
                new { memberUuid = evil, amount = 200_000m }
            }
        });

        var csv = await GetCsvTextAsync(client, $"api/v1/events/{evt}/export");

        // Section 2 balance: the formula member name is neutralized ('=evil), but the money columns
        // (advanced, owed, balance incl. the negative) remain raw invariant decimals.
        Assert.Contains("'=evil,500000.00,200000.00,300000.00", csv);
        Assert.Contains("Tôi,0.00,300000.00,-300000.00", csv);
        Assert.DoesNotContain("'-300000.00", csv); // negative money NOT guarded (OQ5 preserved)

        var balanceTotal = Lines(csv).Single(line =>
            line.StartsWith("Tổng cộng,") && line.EndsWith(",0.00"));
        Assert.EndsWith(",0.00", balanceTotal); // sum-to-zero intact
    }
}
