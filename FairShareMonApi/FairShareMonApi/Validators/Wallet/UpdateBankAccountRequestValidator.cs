using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Wallet;
using FluentValidation;

namespace FairShareMonApi.Validators.Wallet;

/// <summary>
/// Bank account update follows the same field policy as create (OQ5): <c>BankBin</c> <c>^\d{6}$</c>;
/// <c>BankName</c> required + max 100; <c>AccountNumber</c> <c>^\d{6,19}$</c>;
/// <c>AccountHolderName</c> required + max 100. This endpoint cannot change the default flag (OQ6).
/// </summary>
public class UpdateBankAccountRequestValidator : AbstractValidator<UpdateBankAccountRequest>
{
    public UpdateBankAccountRequestValidator()
    {
        RuleFor(request => request.BankBin)
            .NotEmpty().WithMessage("Mã ngân hàng (BIN) không được để trống.")
            .Matches(CreateBankAccountRequestValidator.BankBinPattern).WithMessage("Mã ngân hàng (BIN) phải gồm đúng 6 chữ số.");

        RuleFor(request => request.BankName)
            .NotEmpty().WithMessage("Tên ngân hàng không được để trống.")
            .MaximumLength(BankAccount.BankNameMaxLength)
            .WithMessage($"Tên ngân hàng không được vượt quá {BankAccount.BankNameMaxLength} ký tự.");

        RuleFor(request => request.AccountNumber)
            .NotEmpty().WithMessage("Số tài khoản không được để trống.")
            .Matches(CreateBankAccountRequestValidator.AccountNumberPattern).WithMessage("Số tài khoản không hợp lệ.");

        RuleFor(request => request.AccountHolderName)
            .NotEmpty().WithMessage("Tên chủ tài khoản không được để trống.")
            .MaximumLength(BankAccount.AccountHolderNameMaxLength)
            .WithMessage($"Tên chủ tài khoản không được vượt quá {BankAccount.AccountHolderNameMaxLength} ký tự.");
    }
}
