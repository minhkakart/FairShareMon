using FairShareMonApi.Models.Members;

namespace FairShareMonApi.Models.Shares;

/// <summary>Thông tin một phần gánh trả về cho client.</summary>
public class ShareResponse
{
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Thành viên gánh phần này (hiển thị đầy đủ kể cả khi đã xóa mềm - §4.7).</summary>
    public MemberResponse Member { get; set; } = null!;

    /// <summary>Số tiền gánh (đơn vị VND).</summary>
    public decimal Amount { get; set; }

    public string? Note { get; set; }

    /// <summary>True nếu phần gánh này đã được đánh dấu đã trả (Layer A, §6). Chỉ là metadata thanh toán, không đổi số tiền.</summary>
    public bool IsSettled { get; set; }

    /// <summary>Thời điểm đánh dấu đã trả gần nhất (null nếu chưa đánh dấu).</summary>
    public DateTime? SettledAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
