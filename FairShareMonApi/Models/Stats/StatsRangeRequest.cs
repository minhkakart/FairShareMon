namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Khoảng thời gian cho thống kê tổng quan (§3.9). Cả hai mốc đều tùy chọn (bỏ trống = toàn bộ thời
/// gian); khoảng bao gồm cả hai đầu <c>[from, to]</c>, so sánh theo UTC thô đúng như bộ lọc M5/M6. Nếu
/// có cả hai mà <c>from &gt; to</c> thì bị từ chối (lỗi kiểm tra dữ liệu 1001).
/// </summary>
public class StatsRangeRequest
{
    /// <summary>Lọc từ thời điểm này (bao gồm), tùy chọn.</summary>
    public DateTime? From { get; set; }

    /// <summary>Lọc đến thời điểm này (bao gồm), tùy chọn.</summary>
    public DateTime? To { get; set; }
}
