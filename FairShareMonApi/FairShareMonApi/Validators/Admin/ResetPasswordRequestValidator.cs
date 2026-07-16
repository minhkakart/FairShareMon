using System.Text;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Validators.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// Admin-set temp password follows the same policy as registration (min 8 chars, max 72 bytes) - M11 OQ8.
/// </summary>
public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.NewPassword)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.NewPasswordRequired].Value)
            .MinimumLength(RegisterRequestValidator.PasswordMinLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.NewPasswordTooShort, RegisterRequestValidator.PasswordMinLength].Value)
            .Must(password => Encoding.UTF8.GetByteCount(password ?? string.Empty) <= RegisterRequestValidator.PasswordMaxBytes)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.NewPasswordTooLong, RegisterRequestValidator.PasswordMaxBytes].Value);
    }
}
