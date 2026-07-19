namespace FairShareMonApi.Models.Banks;

/// <summary>
/// Một mục ngân hàng trong danh mục trả về cho client (bộ chọn ngân hàng). URL logo được dựng sẵn ở
/// backend; mã ảnh (<c>imageId</c>) không bao giờ rời khỏi backend.
/// </summary>
public class BankResponse
{
    /// <summary>Mã BIN 6 chữ số của ngân hàng (dùng khi lưu tài khoản ngân hàng và tạo QR).</summary>
    public string Bin { get; set; } = string.Empty;

    /// <summary>Mã ngắn của ngân hàng (ví dụ "TCB") - dùng làm từ khóa tìm kiếm.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên đầy đủ của ngân hàng.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tên viết tắt/thương hiệu của ngân hàng (ví dụ "Techcombank").</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>URL logo ngân hàng đã dựng sẵn để client tải trực tiếp.</summary>
    public string LogoUrl { get; set; } = string.Empty;
}
