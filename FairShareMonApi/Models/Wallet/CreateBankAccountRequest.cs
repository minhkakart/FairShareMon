namespace FairShareMonApi.Models.Wallet;

/// <summary>Yêu cầu thêm tài khoản ngân hàng vào ví.</summary>
public class CreateBankAccountRequest
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
