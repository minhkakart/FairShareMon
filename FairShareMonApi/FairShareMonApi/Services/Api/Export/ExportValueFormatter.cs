using System.Globalization;

namespace FairShareMonApi.Services.Api.Export;

/// <summary>
/// Nơi tập trung DUY NHẤT việc chuyển số tiền/ngày giờ thành chuỗi cho mọi tài liệu xuất (M8, OQ5/OQ6).
/// Mọi phần dựng <c>ExportDocument</c> và mọi formatter hiện tại/tương lai đều đi qua đây để nhất quán.
///
/// <para>CSV được trả về dạng <c>FileContentResult</c> nên KHÔNG đi qua bộ chuyển JSON toàn cục; vì vậy
/// múi giờ phải được áp dụng trực tiếp tại đây bằng <see cref="TimeZoneInfo"/> của request
/// (planning/timezone-aware-datetimes.md, deliverable 11). Dữ liệu lưu UTC, trình bày theo múi giờ đã
/// giải quyết cho request (header <c>X-Time-Zone</c> -&gt; mặc định <c>App:DefaultTimeZone</c>).</para>
///
/// <para><b>Hai quy tắc ngày giờ khác nhau (OQ6 - điểm dễ sinh lỗi nhất của M8):</b></para>
/// <list type="bullet">
/// <item><see cref="FormatInstant"/> - MỐC THỜI GIAN (thời điểm chi, thời điểm tạo/đã trả nếu hiển thị):
/// dữ liệu lưu UTC, đổi sang múi giờ request rồi định dạng <c>dd/MM/yyyy HH:mm</c>.</item>
/// <item><see cref="FormatCalendarDate"/> - NGÀY LỊCH (khoảng thời gian của đợt): mốc trọn ngày được lưu
/// dưới dạng ranh giới UTC đã chuẩn hóa theo múi giờ (D3); đổi ngược về CHÍNH múi giờ đó rồi lấy phần
/// ngày, nên ngày lịch khớp đúng ngày người dùng đã chọn và ranh giới cuối ngày không bị đẩy sang ngày
/// kế tiếp.</item>
/// </list>
/// </summary>
public static class ExportValueFormatter
{
    /// <summary>
    /// Số tiền (OQ5): decimal thô theo văn hóa bất biến, dấu chấm thập phân, hai chữ số thập phân, không
    /// nhóm nghìn (ví dụ <c>800000.00</c>, <c>266.67</c>, <c>0.00</c>).
    /// </summary>
    public static string FormatMoney(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Mốc thời gian (OQ6): đổi UTC sang múi giờ <paramref name="zone"/> rồi định dạng
    /// <c>dd/MM/yyyy HH:mm</c>.
    /// </summary>
    public static string FormatInstant(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utc), zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ngày lịch (OQ6): đổi ranh giới UTC về múi giờ <paramref name="zone"/> rồi định dạng
    /// <c>dd/MM/yyyy</c>. Chỉ dùng cho khoảng thời gian trọn ngày của đợt (đã chuẩn hóa theo cùng múi giờ).
    /// </summary>
    public static string FormatCalendarDate(DateTime utcBoundary, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utcBoundary), zone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Guards <c>ConvertTimeFromUtc</c>, which rejects a <c>Kind.Local</c> source.</summary>
    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
