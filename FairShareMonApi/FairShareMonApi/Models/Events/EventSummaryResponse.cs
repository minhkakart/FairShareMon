namespace FairShareMonApi.Models.Events;

/// <summary>Thông tin tóm tắt đợt chi tiêu cho danh sách (kèm số lượng phiếu suy ra).</summary>
public class EventSummaryResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Ngày bắt đầu (00:00:00 UTC).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Ngày kết thúc (23:59:59.999999 UTC).</summary>
    public DateTime EndDate { get; set; }

    /// <summary>True nếu đợt đã chốt.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Thời điểm chốt đợt. Null khi đợt còn mở.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Số lượng phiếu chi tiêu thuộc đợt (suy ra khi đọc).</summary>
    public int ExpenseCount { get; set; }

    /// <summary>Tổng tiền đã ứng của đợt (tổng tất cả phiếu chi tiêu thuộc đợt = tổng các phần gánh). 0 khi đợt chưa có phiếu.</summary>
    public decimal TotalAdvanced { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Thời điểm hoạt động gần nhất của đợt (UTC): lớn nhất giữa thời điểm cập nhật đợt và thời điểm cập nhật của mọi phiếu/phần gánh thuộc đợt (suy ra khi đọc).</summary>
    public DateTime UpdatedAt { get; set; }
}
