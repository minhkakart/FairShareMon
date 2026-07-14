using System.Globalization;

namespace FairShareMonApi.Services.Api.Export;

/// <summary>
/// Nơi tập trung DUY NHẤT việc chuyển số tiền/ngày giờ thành chuỗi cho mọi tài liệu xuất (M8, OQ5/OQ6).
/// Mọi phần dựng <c>ExportDocument</c> và mọi formatter hiện tại/tương lai đều đi qua đây để nhất quán.
///
/// <para><b>Hai quy tắc ngày giờ khác nhau (OQ6 - điểm dễ sinh lỗi nhất của M8):</b></para>
/// <list type="bullet">
/// <item><see cref="FormatInstant"/> - MỐC THỜI GIAN (thời điểm chi, thời điểm tạo/đã trả nếu hiển thị):
/// dữ liệu lưu UTC, đổi sang UTC+7 cố định (Asia/Ho_Chi_Minh, KHÔNG DST) rồi định dạng
/// <c>dd/MM/yyyy HH:mm</c>.</item>
/// <item><see cref="FormatCalendarDate"/> - NGÀY LỊCH (khoảng thời gian của đợt): dữ liệu lưu là mốc
/// trọn ngày theo UTC (<c>00:00:00</c> / <c>23:59:59.999999</c>), định dạng <c>dd/MM/yyyy</c> LẤY THẲNG
/// phần ngày UTC, KHÔNG cộng +7. Cộng +7 vào <c>23:59:59.999 UTC</c> sẽ đẩy ngày kết thúc sang ngày kế
/// tiếp, nên khoảng thời gian không bao giờ được dịch múi giờ.</item>
/// </list>
/// </summary>
public static class ExportValueFormatter
{
    /// <summary>Lệch múi giờ Việt Nam cố định (+7, không DST) - KHÔNG dùng <see cref="TimeZoneInfo"/> máy chủ.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Số tiền (OQ5): decimal thô theo văn hóa bất biến, dấu chấm thập phân, hai chữ số thập phân, không
    /// nhóm nghìn (ví dụ <c>800000.00</c>, <c>266.67</c>, <c>0.00</c>).
    /// </summary>
    public static string FormatMoney(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Mốc thời gian (OQ6): đổi UTC sang UTC+7 cố định rồi định dạng <c>dd/MM/yyyy HH:mm</c>.
    /// </summary>
    public static string FormatInstant(DateTime utc) =>
        utc.Add(VietnamOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ngày lịch (OQ6): định dạng <c>dd/MM/yyyy</c> lấy thẳng phần ngày UTC, KHÔNG dịch +7. Chỉ dùng cho
    /// khoảng thời gian trọn ngày của đợt.
    /// </summary>
    public static string FormatCalendarDate(DateTime utcBoundary) =>
        utcBoundary.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
