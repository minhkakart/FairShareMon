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

    public DateTime CreatedAt { get; set; }
}
