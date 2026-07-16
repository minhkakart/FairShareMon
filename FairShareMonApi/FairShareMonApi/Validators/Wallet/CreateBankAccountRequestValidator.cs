using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
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

    public CreateBankAccountRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.BankBin)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankBinRequired].Value)
            .Matches(BankBinPattern).WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankBinPattern].Value);

        RuleFor(request => request.BankName)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankNameRequired].Value)
            .MaximumLength(BankAccount.BankNameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.BankNameTooLong, BankAccount.BankNameMaxLength].Value);

        RuleFor(request => request.AccountNumber)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountNumberRequired].Value)
            .Matches(AccountNumberPattern).WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountNumberInvalid].Value);

        RuleFor(request => request.AccountHolderName)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountHolderNameRequired].Value)
            .MaximumLength(BankAccount.AccountHolderNameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.BankAccount.AccountHolderNameTooLong, BankAccount.AccountHolderNameMaxLength].Value);
    }
}
