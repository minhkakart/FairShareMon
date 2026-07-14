using FairShareMonApi.Models.Categories;
using FairShareMonApi.Models.Members;

namespace FairShareMonApi.Models.Expenses;

/// <summary>Thông tin tóm tắt phiếu chi tiêu cho danh sách (OQ13).</summary>
public class ExpenseSummaryResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime ExpenseTime { get; set; }

    /// <summary>Tổng tiền = tổng các phần gánh (suy ra khi đọc, OQ1).</summary>
    public decimal Total { get; set; }

    /// <summary>Danh mục của phiếu (hiển thị đầy đủ kể cả khi đã xóa mềm - §4.7).</summary>
    public CategoryResponse Category { get; set; } = null!;

    /// <summary>Thành viên trả tiền (hiển thị đầy đủ kể cả khi đã xóa mềm - §4.7).</summary>
    public MemberResponse Payer { get; set; } = null!;

    public bool IsSettled { get; set; }

    public DateTime? SettledAt { get; set; }

    /// <summary>Tên các nhãn của phiếu.</summary>
    public IReadOnlyList<string> TagNames { get; set; } = [];

    /// <summary>Số lượng phần gánh.</summary>
    public int ShareCount { get; set; }

    /// <summary>UUID đợt chi tiêu của phiếu; null nếu phiếu chưa thuộc đợt nào (M6, OQ14).</summary>
    public string? EventUuid { get; set; }

    /// <summary>Tên đợt chi tiêu của phiếu; null nếu chưa thuộc đợt nào (M6, OQ14).</summary>
    public string? EventName { get; set; }

    /// <summary>Trạng thái đã chốt của đợt; null nếu chưa thuộc đợt nào (M6, OQ14).</summary>
    public bool? EventIsClosed { get; set; }

    public DateTime CreatedAt { get; set; }
}
