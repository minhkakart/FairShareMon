namespace FairShareMonApi.Models.Wallet;

/// <summary>Thông tin tài khoản ngân hàng trong ví trả về cho client.</summary>
public class BankAccountResponse
{
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Mã ngân hàng (NAPAS BIN).</summary>
    public string BankBin { get; set; } = string.Empty;

    /// <summary>Tên ngân hàng hiển thị.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Số tài khoản nhận.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Tên chủ tài khoản.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>True nếu là tài khoản mặc định (đích nhận mặc định khi tạo mã QR).</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }
}
