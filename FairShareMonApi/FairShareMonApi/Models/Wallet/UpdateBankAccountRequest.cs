namespace FairShareMonApi.Models.Wallet;

/// <summary>Yêu cầu cập nhật tài khoản ngân hàng. Không đổi cờ mặc định qua endpoint này (dùng đặt tài khoản mặc định).</summary>
public class UpdateBankAccountRequest
{
    /// <summary>Mã ngân hàng (NAPAS BIN) gồm đúng 6 chữ số.</summary>
    public string BankBin { get; set; } = string.Empty;

    /// <summary>Tên ngân hàng hiển thị (tối đa 100 ký tự).</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Số tài khoản nhận (chỉ chữ số, 6-19 ký tự).</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Tên chủ tài khoản (tối đa 100 ký tự).</summary>
    public string AccountHolderName { get; set; } = string.Empty;
}
