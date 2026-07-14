namespace FairShareMonApi.Models.Admin;

/// <summary>
/// Tham số cho bảng chỉ số quản trị (M11 OQ6). <c>From</c>/<c>To</c> giới hạn khoảng cho biểu đồ đăng ký
/// theo thời gian (cả hai tùy chọn = toàn bộ thời gian, bao gồm cả hai đầu, so sánh UTC); <c>Bucket</c> =
/// month (mặc định) hoặc day. Các phân bố hạng/vai trò/trạng thái luôn tính trên toàn bộ người dùng.
/// </summary>
public class AdminMetricsRequest
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    /// <summary>Độ chia thời gian cho biểu đồ đăng ký: month (mặc định) | day.</summary>
    public string Bucket { get; set; } = "month";
}
