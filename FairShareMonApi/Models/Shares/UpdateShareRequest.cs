namespace FairShareMonApi.Models.Shares;

/// <summary>Yêu cầu cập nhật một phần gánh (số tiền, ghi chú, đổi thành viên).</summary>
public class UpdateShareRequest
{
    /// <summary>UUID thành viên gánh phần này (có thể đổi sang thành viên khác).</summary>
    public string MemberUuid { get; set; } = string.Empty;

    /// <summary>Số tiền gánh (không âm, đơn vị VND).</summary>
    public decimal Amount { get; set; }

    /// <summary>Ghi chú tùy chọn (tối đa 500 ký tự).</summary>
    public string? Note { get; set; }
}
