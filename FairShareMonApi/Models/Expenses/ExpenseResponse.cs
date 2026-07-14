using FairShareMonApi.Models.Categories;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Models.Tags;

namespace FairShareMonApi.Models.Expenses;

/// <summary>Thông tin đầy đủ của phiếu chi tiêu (gồm phần gánh, nhãn, tổng tiền suy ra) - OQ13.</summary>
public class ExpenseResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime ExpenseTime { get; set; }

    /// <summary>Tổng tiền = tổng các phần gánh (suy ra khi đọc, OQ1).</summary>
    public decimal Total { get; set; }

    /// <summary>Danh mục của phiếu (hiển thị đầy đủ kể cả khi đã xóa mềm - §4.7).</summary>
    public CategoryResponse Category { get; set; } = null!;

    /// <summary>Thành viên trả tiền (hiển thị đầy đủ kể cả khi đã xóa mềm - §4.7).</summary>
    public MemberResponse Payer { get; set; } = null!;

    public bool IsSettled { get; set; }

    public DateTime? SettledAt { get; set; }

    /// <summary>Các phần gánh của phiếu.</summary>
    public IReadOnlyList<ShareResponse> Shares { get; set; } = [];

    /// <summary>Các nhãn của phiếu (hiển thị đầy đủ kể cả khi đã xóa mềm - §4.7).</summary>
    public IReadOnlyList<TagResponse> Tags { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
