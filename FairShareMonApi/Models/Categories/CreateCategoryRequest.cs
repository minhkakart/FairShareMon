namespace FairShareMonApi.Models.Categories;

/// <summary>Yêu cầu thêm danh mục chi tiêu mới.</summary>
public class CreateCategoryRequest
{
    /// <summary>Tên danh mục (1-100 ký tự).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Màu danh mục dạng hex <c>#RRGGBB</c> (dùng cho biểu đồ).</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>Khóa icon tùy chọn (tối đa 50 ký tự) do client ánh xạ sang biểu tượng.</summary>
    public string? Icon { get; set; }
}
