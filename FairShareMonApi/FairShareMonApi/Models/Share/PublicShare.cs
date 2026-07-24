namespace FairShareMonApi.Models.Share;

/// <summary>Một phần gánh trong bản báo cáo chia sẻ công khai (chỉ xem).</summary>
public class PublicShare
{
    public string MemberUuid { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;

    /// <summary>Số tiền gánh (VND).</summary>
    public decimal Amount { get; set; }

    /// <summary>True nếu phần gánh này đã được đánh dấu đã trả.</summary>
    public bool IsSettled { get; set; }

    public string? Note { get; set; }
}
