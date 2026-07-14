namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Thống kê chi tiêu của một danh mục (§3.9): tổng tiền + số lượng phiếu, kèm màu/icon để vẽ biểu đồ
/// tròn/cột. Danh mục đã xóa mềm nhưng có phiếu lịch sử vẫn xuất hiện (cờ <see cref="IsDeleted"/> - §4.7).
/// </summary>
public class CategoryStatRow
{
    public string CategoryUuid { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Màu danh mục dạng hex <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>Khóa icon tùy chọn do client ánh xạ.</summary>
    public string? Icon { get; set; }

    /// <summary>True nếu danh mục đã bị xóa mềm (vẫn hiển thị trong thống kê lịch sử - §4.7).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Tổng chi tiêu của danh mục trong phạm vi = tổng các phần gánh.</summary>
    public decimal Total { get; set; }

    /// <summary>Số lượng phiếu thuộc danh mục trong phạm vi.</summary>
    public int ExpenseCount { get; set; }
}
