namespace FairShareMonApi.Models.Shares;

/// <summary>Yêu cầu thêm một phần gánh vào phiếu chi tiêu.</summary>
public class CreateShareRequest
{
    /// <summary>UUID thành viên gánh phần này.</summary>
    public string MemberUuid { get; set; } = string.Empty;

    /// <summary>Số tiền gánh (không âm, đơn vị VND).</summary>
    public decimal Amount { get; set; }

    /// <summary>Ghi chú tùy chọn (tối đa 500 ký tự).</summary>
    public string? Note { get; set; }
}
