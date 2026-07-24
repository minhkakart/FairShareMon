namespace FairShareMonApi.Models.Wallet;

/// <summary>
/// Ảnh chụp bất biến của tài khoản ngân hàng đích, được sao chép lên liên kết chia sẻ tại thời điểm
/// tạo (planning/event-share-link.md, Decision 7). Dùng để dựng mã QR ổn định kể cả khi ví bị sửa hay
/// xóa cứng. Không có bản ghi DB tương ứng - <c>WalletQrService</c> dựng một <c>BankAccount</c> tạm
/// thời (không lưu) từ ảnh chụp này.
/// </summary>
public sealed record BankSnapshot(string BankBin, string BankName, string AccountNumber, string AccountHolderName);
