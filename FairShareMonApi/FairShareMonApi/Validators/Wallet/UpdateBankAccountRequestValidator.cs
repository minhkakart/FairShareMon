using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Wallet;

/// <summary>
/// Bank account update follows the same field policy as create (OQ5): <c>BankBin</c> <c>^\d{6}$</c>;
/// <c>BankName</c> required + max 100; <c>AccountNumber</c> <c>^\d{6,19}$</c>;
/// <c>AccountHolderName</c> required + max 100. This endpoint cannot change the default flag (OQ6).
/// </summary>
public class UpdateBankAccountRequestValidator : AbstractValidator<UpdateBankAccountRequest>
{
    public UpdateBankAccountRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.BankBin)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankBinRequired].Value)
            .Matches(CreateBankAccountRequestValidator.BankBinPattern).WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankBinPattern].Value);

        RuleFor(request => request.BankName)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankNameRequired].Value)
            .MaximumLength(BankAccount.BankNameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankNameTooLong, BankAccount.BankNameMaxLength].Value);

        RuleFor(request => request.AccountNumber)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountNumberRequired].Value)
            .Matches(CreateBankAccountRequestValidator.AccountNumberPattern).WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountNumberInvalid].Value);

        RuleFor(request => request.AccountHolderName)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountHolderNameRequired].Value)
            .MaximumLength(BankAccount.AccountHolderNameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountHolderNameTooLong, BankAccount.AccountHolderNameMaxLength].Value);
    }
}
