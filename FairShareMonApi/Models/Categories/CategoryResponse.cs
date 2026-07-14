namespace FairShareMonApi.Models.Categories;

/// <summary>Thông tin danh mục chi tiêu trả về cho client.</summary>
public class CategoryResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Màu danh mục dạng hex <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>Khóa icon tùy chọn do client ánh xạ sang biểu tượng.</summary>
    public string? Icon { get; set; }

    /// <summary>True nếu là danh mục mặc định (mỗi sổ luôn có đúng một, không thể xóa).</summary>
    public bool IsDefault { get; set; }

    /// <summary>True nếu danh mục đã bị xóa mềm (chỉ hiện khi yêu cầu kèm danh mục đã xóa).</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }
}
