using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Wallet;
using FluentValidation;

namespace FairShareMonApi.Validators.Wallet;

/// <summary>
/// Bank account create rules (OQ5): <c>BankBin</c> exactly 6 digits (<c>^\d{6}$</c>); <c>BankName</c>
/// required + max 100; <c>AccountNumber</c> digits only 6-19 (<c>^\d{6,19}$</c>);
/// <c>AccountHolderName</c> required + max 100. The service trims text before persisting.
/// </summary>
public class CreateBankAccountRequestValidator : AbstractValidator<CreateBankAccountRequest>
{
    /// <summary>NAPAS BIN pattern - exactly 6 digits (OQ5).</summary>
    public const string BankBinPattern = @"^\d{6}$";

    /// <summary>Account-number pattern - digits only, 6-19 chars (OQ5).</summary>
    public const string AccountNumberPattern = @"^\d{6,19}$";

    public CreateBankAccountRequestValidator()
    {
        RuleFor(request => request.BankBin)
            .NotEmpty().WithMessage("Mã ngân hàng (BIN) không được để trống.")
            .Matches(BankBinPattern).WithMessage("Mã ngân hàng (BIN) phải gồm đúng 6 chữ số.");

        RuleFor(request => request.BankName)
            .NotEmpty().WithMessage("Tên ngân hàng không được để trống.")
            .MaximumLength(BankAccount.BankNameMaxLength)
            .WithMessage($"Tên ngân hàng không được vượt quá {BankAccount.BankNameMaxLength} ký tự.");

        RuleFor(request => request.AccountNumber)
            .NotEmpty().WithMessage("Số tài khoản không được để trống.")
            .Matches(AccountNumberPattern).WithMessage("Số tài khoản không hợp lệ.");

        RuleFor(request => request.AccountHolderName)
            .NotEmpty().WithMessage("Tên chủ tài khoản không được để trống.")
            .MaximumLength(BankAccount.AccountHolderNameMaxLength)
            .WithMessage($"Tên chủ tài khoản không được vượt quá {BankAccount.AccountHolderNameMaxLength} ký tự.");
    }
}
