namespace FairShareMonApi.Models.Share;

/// <summary>Thông tin liên kết chia sẻ trả về cho chủ sổ (để xem/sao chép). Frontend dựng URL công khai từ <see cref="Token"/>.</summary>
public class ShareLinkResponse
{
    /// <summary>Token công khai của liên kết (giá trị gốc, có thể xem/sao chép lại).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Thời điểm liên kết hết hạn (cố định theo thời điểm tạo, không gia hạn khi xem lại).</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>True nếu liên kết kèm ảnh chụp ngân hàng (có thể tạo mã QR); false nếu không.</summary>
    public bool HasQr { get; set; }

    /// <summary>Tên ngân hàng đã ảnh chụp (null nếu không kèm ảnh chụp).</summary>
    public string? BankName { get; set; }

    /// <summary>Số tài khoản đã ảnh chụp (null nếu không kèm ảnh chụp).</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Tên chủ tài khoản đã ảnh chụp (null nếu không kèm ảnh chụp).</summary>
    public string? AccountHolderName { get; set; }
}
