namespace FairShareMonApi.Models.Share;

/// <summary>Một phiếu chi tiêu (kèm các phần gánh) trong bản báo cáo chia sẻ công khai (chỉ xem).</summary>
public class PublicExpense
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>UUID thành viên trả tiền phiếu này.</summary>
    public string PayerMemberUuid { get; set; } = string.Empty;

    /// <summary>Tên thành viên trả tiền phiếu này.</summary>
    public string PayerName { get; set; } = string.Empty;

    public DateTime ExpenseTime { get; set; }

    /// <summary>Tổng tiền phiếu = tổng các phần gánh.</summary>
    public decimal Total { get; set; }

    /// <summary>Các phần gánh của phiếu.</summary>
    public IReadOnlyList<PublicShare> Shares { get; set; } = [];
}
