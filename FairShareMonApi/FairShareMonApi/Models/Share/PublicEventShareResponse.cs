using FairShareMonApi.Models.Stats;

namespace FairShareMonApi.Models.Share;

/// <summary>
/// Bản báo cáo chia sẻ công khai (chỉ xem) của một đợt đã chốt (planning/event-share-link.md). Đây là
/// bản đọc <b>trực tiếp</b>: số liệu chi tiêu của đợt đã chốt là cố định nhưng lớp đã trả/còn nợ được
/// tính lại trên mỗi lần đọc. Bất kỳ ai có token đều xem được, không cần tài khoản.
/// </summary>
public class PublicEventShareResponse
{
    public string EventName { get; set; } = string.Empty;

    /// <summary>Thời điểm chốt đợt.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Cân bằng theo từng thành viên tham gia đợt (dùng lại từ thống kê M7).</summary>
    public IReadOnlyList<MemberBalanceRow> Rows { get; set; } = [];

    /// <summary>Danh sách phiếu chi tiêu của đợt kèm phần gánh chi tiết.</summary>
    public IReadOnlyList<PublicExpense> Expenses { get; set; } = [];

    /// <summary>Tổng số tiền còn nợ chưa trả của cả đợt.</summary>
    public decimal TotalOutstanding { get; set; }

    /// <summary>Số thành viên còn nợ chưa trả.</summary>
    public int OwingMemberCount { get; set; }

    /// <summary>Số thành viên đang nợ nhưng đã được đánh dấu đã trả.</summary>
    public int SettledMemberCount { get; set; }

    /// <summary>True nếu liên kết kèm ảnh chụp ngân hàng (có thể lấy mã QR theo từng thành viên).</summary>
    public bool HasQr { get; set; }
}
