namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Cân bằng nợ của một thành viên trong một đợt (§3.7). Tên thành viên được ghi kèm (denormalized) để
/// thành viên đã xóa mềm vẫn hiển thị đầy đủ (§4.7). balance = advanced - owed; dương nghĩa là người
/// khác đang nợ thành viên này, âm nghĩa là thành viên này đang nợ.
/// </summary>
public class MemberBalanceRow
{
    public string MemberUuid { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;

    /// <summary>True nếu là thành viên đại diện chủ sổ.</summary>
    public bool IsOwnerRepresentative { get; set; }

    /// <summary>True nếu thành viên đã bị xóa mềm (vẫn hiển thị trong báo cáo lịch sử - §4.7).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Tổng tiền thành viên đã ứng (tổng các phần gánh của những phiếu do thành viên này trả).</summary>
    public decimal Advanced { get; set; }

    /// <summary>Tổng tiền thành viên phải gánh (tổng các phần gánh của thành viên này).</summary>
    public decimal Owed { get; set; }

    /// <summary>Cân bằng = đã ứng - phải gánh.</summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// Số tiền còn nợ ròng chưa trả (overlay suy ra, §6). = -balance khi thành viên còn nợ (balance &lt; 0)
    /// và chưa đánh dấu đã trả; = 0 khi đã đánh dấu đã trả hoặc không nợ (balance ≥ 0). Không làm thay đổi
    /// balance (D2).
    /// </summary>
    public decimal Outstanding { get; set; }

    /// <summary>True nếu thành viên đã được đánh dấu đã trả khoản nợ ròng trong đợt này (Layer B, §3.7/§6).</summary>
    public bool IsSettled { get; set; }

    /// <summary>Thời điểm đánh dấu đã trả khoản nợ ròng gần nhất (null nếu chưa đánh dấu).</summary>
    public DateTime? SettledAt { get; set; }

    /// <summary>
    /// True nếu thành viên đủ điều kiện để việc đánh dấu "đã trả" ở cấp đợt tự động lan xuống mọi phần
    /// gánh của họ trong đợt (chỉ dành cho người nợ ròng, hoặc người được nợ ròng nhưng không gánh khoản
    /// nợ nào khác trong đợt).
    /// </summary>
    public bool IsEligibleForAutoCascade { get; set; }

    /// <summary>
    /// Số tiền đã tất toán lũy kế cho khoản nợ ròng của thành viên này (event-expense-settlement-sync M2):
    /// được ghi nhận khi đánh dấu đã trả thủ công ở cấp đợt (= toàn bộ số nợ ròng) và/hoặc khi đánh dấu đã
    /// trả từng phần gánh/phiếu chi tiêu riêng lẻ (mỗi lần cộng thêm đúng số tiền phần gánh đó, giới hạn
    /// không vượt quá số nợ ròng hiện tại và không âm). Là nguồn dữ liệu duy nhất mà <see cref="Outstanding"/>
    /// và <see cref="SettlementStatus"/> được suy ra từ đó.
    /// </summary>
    public decimal ClearedAmount { get; set; }

    /// <summary>
    /// Trạng thái tất toán khoản nợ ròng của thành viên (suy ra, không lưu trữ):
    /// <c>"Unsettled"</c> (chưa trả - không nợ ròng, hoặc nợ ròng nhưng chưa tất toán gì),
    /// <c>"PartiallySettled"</c> (đã trả một phần), <c>"Settled"</c> (đã trả hết). Kiểu chuỗi trên API
    /// (không phải enum thô - xem Decision Log entry 6 của tài liệu kế hoạch).
    /// </summary>
    public string SettlementStatus { get; set; } = string.Empty;
}
