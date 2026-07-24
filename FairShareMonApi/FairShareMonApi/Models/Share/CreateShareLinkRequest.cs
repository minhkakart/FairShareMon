namespace FairShareMonApi.Models.Share;

/// <summary>Yêu cầu tạo liên kết chia sẻ công khai (chỉ xem) cho một đợt đã chốt.</summary>
public class CreateShareLinkRequest
{
    /// <summary>
    /// UUID tài khoản ngân hàng đích để ảnh chụp cho mã QR (tùy chọn). Bỏ trống sẽ dùng tài khoản mặc
    /// định nếu có; nếu không có tài khoản nào, liên kết được tạo mà không kèm mã QR (hasQr = false).
    /// </summary>
    public string? BankAccountUuid { get; set; }

    /// <summary>
    /// True để tạo lại: thu hồi liên kết đang hoạt động rồi cấp token mới trong cùng một giao dịch.
    /// False (mặc định) sẽ tái sử dụng liên kết đang hoạt động nếu có (bỏ qua bankAccountUuid khác).
    /// </summary>
    public bool Regenerate { get; set; }
}
