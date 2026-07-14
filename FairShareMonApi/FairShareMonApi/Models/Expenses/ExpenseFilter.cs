namespace FairShareMonApi.Models.Expenses;

/// <summary>
/// Bộ lọc danh sách phiếu chi tiêu (kết hợp AND, OQ13).
/// </summary>
public class ExpenseFilter
{
    /// <summary>Lọc từ thời điểm này (bao gồm), tùy chọn.</summary>
    public DateTime? From { get; set; }

    /// <summary>Lọc đến thời điểm này (bao gồm), tùy chọn.</summary>
    public DateTime? To { get; set; }

    /// <summary>Lọc theo UUID danh mục, tùy chọn.</summary>
    public string? CategoryUuid { get; set; }

    /// <summary>Lọc theo UUID nhãn, tùy chọn.</summary>
    public string? TagUuid { get; set; }

    /// <summary>Lọc theo trạng thái đã trả, tùy chọn.</summary>
    public bool? Settled { get; set; }

    /// <summary>Lọc theo UUID đợt chi tiêu, tùy chọn (M6, OQ14).</summary>
    public string? EventUuid { get; set; }

    /// <summary>Chỉ lấy phiếu chưa thuộc đợt nào (loose), tùy chọn (M6, OQ14).</summary>
    public bool? LooseOnly { get; set; }
}
