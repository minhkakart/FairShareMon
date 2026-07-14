namespace FairShareMonApi.Models.Expenses;

/// <summary>Yêu cầu cập nhật thông tin chung của phiếu chi tiêu (không sửa phần gánh - dùng các endpoint phần gánh riêng).</summary>
public class UpdateExpenseRequest
{
    /// <summary>Tên phiếu chi tiêu (1-200 ký tự).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mô tả tùy chọn (tối đa 1000 ký tự).</summary>
    public string? Description { get; set; }

    /// <summary>Thời điểm chi.</summary>
    public DateTime ExpenseTime { get; set; }

    /// <summary>UUID thành viên trả tiền. Bỏ trống để mặc định là thành viên đại diện chủ sổ.</summary>
    public string? PayerMemberUuid { get; set; }

    /// <summary>UUID danh mục. Bỏ trống để dùng danh mục mặc định.</summary>
    public string? CategoryUuid { get; set; }

    /// <summary>Danh sách UUID nhãn đầy đủ (thay thế toàn bộ tập nhãn hiện tại).</summary>
    public IReadOnlyList<string>? TagUuids { get; set; }
}
