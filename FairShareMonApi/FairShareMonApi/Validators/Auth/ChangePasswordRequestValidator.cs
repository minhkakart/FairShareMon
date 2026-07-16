using System.Text;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Auth;

/// <summary>
/// New password follows the same policy as registration (min 8 chars, max 72 bytes - OQ3).
/// Reusing the current password is deliberately allowed (OQ3 decision) - no equality rule.
/// </summary>
public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.CurrentPassword)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.CurrentPasswordRequired].Value);

        RuleFor(request => request.NewPassword)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.NewPasswordRequired].Value)
            .MinimumLength(RegisterRequestValidator.PasswordMinLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.NewPasswordTooShort, RegisterRequestValidator.PasswordMinLength].Value)
            .Must(password => Encoding.UTF8.GetByteCount(password ?? string.Empty) <= RegisterRequestValidator.PasswordMaxBytes)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.NewPasswordTooLong, RegisterRequestValidator.PasswordMaxBytes].Value);
    }
}
