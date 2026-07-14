namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Cân bằng nợ của một đợt chi tiêu (§3.7). Gồm thông tin đợt và danh sách cân bằng theo từng thành
/// viên tham gia (người trả hoặc người gánh, kể cả thành viên đại diện ở mức 0đ và thành viên đã xóa
/// mềm). Tổng balance của tất cả thành viên luôn bằng 0. Đợt chưa có phiếu nào -&gt; danh sách rỗng.
/// </summary>
public class EventBalanceResponse
{
    public string EventUuid { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;

    /// <summary>True nếu đợt đã chốt (cân bằng vẫn xem được cho cả đợt đang mở và đã chốt).</summary>
    public bool IsClosed { get; set; }

    /// <summary>Cân bằng theo từng thành viên tham gia đợt.</summary>
    public IReadOnlyList<MemberBalanceRow> Rows { get; set; } = [];
}
