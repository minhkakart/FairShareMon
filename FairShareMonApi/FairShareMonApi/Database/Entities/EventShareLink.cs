using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Liên kết chia sẻ công khai (chỉ xem) của một đợt chi tiêu đã chốt (bảng
/// <c>event_share_links</c>, planning/event-share-link.md). Bất kỳ ai có <see cref="Token"/> đều có
/// thể xem báo cáo đợt mà không cần tài khoản; liên kết tự hết hạn sau 1 ngày
/// (<see cref="ExpiresAt"/>) và có thể bị thu hồi mềm (<see cref="RevokedAt"/>, giữ hàng đến khi hết
/// hạn tự nhiên). Token được lưu ở dạng <b>giá trị gốc</b> (unique) để chủ sổ xem/sao chép lại
/// (Decision 6). Các cột ảnh chụp ngân hàng (BIN/tên/số tài khoản/chủ tài khoản) là NULLABLE (OQ4b):
/// khi tài khoản đích được ảnh chụp, mã QR ổn định kể cả khi ví bị sửa/xóa cứng (Decision 7);
/// <see cref="BankAccountUuid"/> chỉ là tham chiếu mềm. Thuộc về đúng một <see cref="User"/> và một
/// <see cref="Event"/> (cả hai FK cascade). Không phải <see cref="IEntityDeletable"/> (thu hồi mềm
/// dùng <see cref="RevokedAt"/>).
/// </summary>
public partial class EventShareLink : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Chủ sở hữu liên kết (FK -&gt; <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Đợt chi tiêu được chia sẻ (FK -&gt; <c>events.id</c>, cascade delete).</summary>
    public ulong EventId { get; set; }

    /// <summary>Token công khai (giá trị gốc, unique) dùng để mở báo cáo chia sẻ.</summary>
    public required string Token { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Null = còn hiệu lực. Được đặt khi thu hồi (thu hồi mềm; hàng giữ đến khi hết hạn).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Tham chiếu mềm tới tài khoản ngân hàng đích (có thể null nếu không có ảnh chụp).</summary>
    public string? BankAccountUuid { get; set; }

    /// <summary>Ảnh chụp mã ngân hàng (BIN). Null khi liên kết không kèm ảnh chụp ngân hàng (OQ4b).</summary>
    public string? BankBin { get; set; }

    /// <summary>Ảnh chụp tên ngân hàng hiển thị. Null khi không kèm ảnh chụp.</summary>
    public string? BankName { get; set; }

    /// <summary>Ảnh chụp số tài khoản nhận. Null khi không kèm ảnh chụp.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Ảnh chụp tên chủ tài khoản. Null khi không kèm ảnh chụp.</summary>
    public string? AccountHolderName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Event Event { get; set; } = null!;
}
