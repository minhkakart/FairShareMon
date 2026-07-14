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
}
