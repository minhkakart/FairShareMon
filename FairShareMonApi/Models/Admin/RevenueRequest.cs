namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Tham số cho bảng doanh thu (M11 OQ14). Doanh thu = tổng <c>amount</c> của các dòng GRANT trong khoảng
/// <c>[From,To]</c> UTC (cả hai tùy chọn = toàn bộ thời gian, bao gồm cả hai đầu); <c>Bucket</c> = month
/// (mặc định) hoặc day. Chỉ tính trên <c>tier_grants</c> - không đụng tới bảng sổ chi tiêu (R10).
/// </summary>
public class RevenueRequest
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    /// <summary>Độ chia thời gian: month (mặc định) | day.</summary>
    public string Bucket { get; set; } = "month";
}
