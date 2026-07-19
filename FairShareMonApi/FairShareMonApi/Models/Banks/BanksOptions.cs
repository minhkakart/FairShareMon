namespace FairShareMonApi.Models.Banks;

/// <summary>
/// Cấu hình cho danh mục ngân hàng và nguồn tạo nội dung VietQR (mục <c>Banks</c> trong appsettings).
/// Bao gồm nhà cung cấp nội dung QR đang chọn và các đường dẫn của VietQR để lấy danh mục, tạo QR và
/// dựng URL logo.
/// </summary>
public class BanksOptions
{
    /// <summary>Tên mục cấu hình trong appsettings.</summary>
    public const string SectionName = "Banks";

    /// <summary>Nhà cung cấp nội dung QR đang chọn: <c>Local</c> (mặc định) hoặc <c>VietQr</c>.</summary>
    public string QrProvider { get; set; } = "Local";

    /// <summary>Cấu hình các đường dẫn của nhà cung cấp VietQR.</summary>
    public VietQrOptions VietQr { get; set; } = new();
}

/// <summary>Các đường dẫn của VietQR dùng để lấy danh mục ngân hàng, tạo QR và dựng URL logo.</summary>
public class VietQrOptions
{
    /// <summary>Địa chỉ gốc của VietQR.</summary>
    public string BaseUrl { get; set; } = "https://vietqr.vn";

    /// <summary>Đường dẫn lấy danh mục ngân hàng.</summary>
    public string BanksPath { get; set; } = "/api/vietqr/banks";

    /// <summary>Đường dẫn tạo nội dung QR.</summary>
    public string GeneratePath { get; set; } = "/api/vietqr/generate";

    /// <summary>Đường dẫn ảnh logo ngân hàng (URL logo = <c>{BaseUrl}{ImagePath}/{imageId}</c>).</summary>
    public string ImagePath { get; set; } = "/api/vietqr/images";
}
