namespace FairShareMonApi.Models.Expenses;

/// <summary>Yêu cầu tạo phiếu chi tiêu mới (tạo cùng các phần gánh trong một giao dịch).</summary>
public class CreateExpenseRequest
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

    /// <summary>Danh sách UUID các nhãn (tùy chọn).</summary>
    public IReadOnlyList<string>? TagUuids { get; set; }

    /// <summary>Danh sách phần gánh. Phần gánh của thành viên đại diện chủ sổ sẽ được tự thêm ở mức 0đ nếu thiếu.</summary>
    public IReadOnlyList<CreateShareInput>? Shares { get; set; }
}

/// <summary>Một phần gánh khi tạo phiếu chi tiêu.</summary>
public class CreateShareInput
{
    /// <summary>UUID thành viên gánh phần này.</summary>
    public string MemberUuid { get; set; } = string.Empty;

    /// <summary>Số tiền gánh (không âm, đơn vị VND).</summary>
    public decimal Amount { get; set; }

    /// <summary>Ghi chú tùy chọn (tối đa 500 ký tự).</summary>
    public string? Note { get; set; }
}
