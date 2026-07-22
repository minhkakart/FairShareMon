namespace FairShareMonApi.Models.Wallet;

/// <summary>
/// Mã QR chuyển khoản riêng của một thành viên còn nợ trên phiếu chi tiêu hoặc đợt đã chốt.
/// Mỗi thành viên còn nợ tương ứng một phần tử trong danh sách trả về; frontend nạp thẳng
/// <see cref="Image"/> vào thẻ <c>img</c> để hiển thị một mã QR cho mỗi thành viên.
/// </summary>
public sealed class MemberQrResponse
{
    /// <summary>UUID của thành viên còn nợ.</summary>
    public string MemberUuid { get; set; } = string.Empty;

    /// <summary>Tên hiển thị của thành viên (đã sao chép sẵn để hiển thị).</summary>
    public string MemberName { get; set; } = string.Empty;

    /// <summary>Số tiền còn nợ của thành viên (VND).</summary>
    public decimal Amount { get; set; }

    /// <summary>Ảnh QR của thành viên dưới dạng data URL <c>data:image/png;base64,&lt;...&gt;</c>.</summary>
    public string Image { get; set; } = string.Empty;
}
