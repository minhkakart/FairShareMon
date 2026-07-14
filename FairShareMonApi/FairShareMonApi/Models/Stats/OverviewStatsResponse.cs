namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Thống kê tổng quan (§3.9): tổng chi tiêu và số lượng phiếu trong một khoảng thời gian trên toàn bộ
/// sổ (phiếu thuộc đợt lẫn phiếu rời). Lặp lại <c>from</c>/<c>to</c> đã dùng. Khoảng rỗng -&gt; các số
/// bằng 0.
/// </summary>
public class OverviewStatsResponse
{
    /// <summary>Mốc bắt đầu đã dùng (null nếu không giới hạn).</summary>
    public DateTime? From { get; set; }

    /// <summary>Mốc kết thúc đã dùng (null nếu không giới hạn).</summary>
    public DateTime? To { get; set; }

    /// <summary>Tổng chi tiêu = tổng các phần gánh của mọi phiếu trong khoảng.</summary>
    public decimal TotalSpending { get; set; }

    /// <summary>Số lượng phiếu chi tiêu trong khoảng.</summary>
    public int ExpenseCount { get; set; }
}
