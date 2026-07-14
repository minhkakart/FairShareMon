namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Bộ lọc thống kê theo danh mục (§3.9): theo khoảng thời gian HOẶC theo một đợt (<c>eventUuid</c>) -
/// không dùng đồng thời (OQ8). Khi có <c>eventUuid</c> thì bỏ qua khoảng thời gian và giới hạn trong
/// đợt (đợt phải thuộc sở hữu, nếu không -&gt; 404/9000). Gửi cả hai -&gt; lỗi kiểm tra dữ liệu (1001).
/// Khoảng thời gian bao gồm cả hai đầu, so sánh UTC thô như M5/M6.
/// </summary>
public class ByCategoryStatsRequest
{
    /// <summary>Lọc từ thời điểm này (bao gồm), tùy chọn.</summary>
    public DateTime? From { get; set; }

    /// <summary>Lọc đến thời điểm này (bao gồm), tùy chọn.</summary>
    public DateTime? To { get; set; }

    /// <summary>UUID đợt chi tiêu (chế độ theo đợt), tùy chọn. Không dùng chung với from/to.</summary>
    public string? EventUuid { get; set; }
}
