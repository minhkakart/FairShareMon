namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Thống kê theo danh mục (§3.9): danh sách các danh mục có ít nhất một phiếu trong phạm vi, sắp xếp
/// theo tổng tiền giảm dần (rồi số phiếu giảm dần, rồi tên). Lặp lại phạm vi đã dùng (<c>eventUuid</c>
/// hoặc <c>from</c>/<c>to</c>). Phạm vi rỗng -&gt; danh sách rỗng.
/// </summary>
public class ByCategoryStatsResponse
{
    /// <summary>UUID đợt đã dùng (chế độ theo đợt); null nếu lọc theo khoảng thời gian.</summary>
    public string? EventUuid { get; set; }

    /// <summary>Mốc bắt đầu đã dùng (chế độ theo thời gian); null ở chế độ theo đợt hoặc không giới hạn.</summary>
    public DateTime? From { get; set; }

    /// <summary>Mốc kết thúc đã dùng (chế độ theo thời gian); null ở chế độ theo đợt hoặc không giới hạn.</summary>
    public DateTime? To { get; set; }

    /// <summary>Thống kê theo từng danh mục.</summary>
    public IReadOnlyList<CategoryStatRow> Rows { get; set; } = [];
}
