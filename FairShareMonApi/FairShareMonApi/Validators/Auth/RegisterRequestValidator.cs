using System.Text;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Auth;

/// <summary>
/// Username: 3-32 chars of <c>a-z 0-9 _ . -</c> (uppercase accepted, stored lowercase - OQ2).
/// Password: min 8 chars, max 72 BYTES (BCrypt truncation limit), no composition rules (OQ3).
/// </summary>
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 32;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxBytes = 72;
    public const string UsernamePattern = "^[a-zA-Z0-9_.-]+$";

    public RegisterRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Username)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.UsernameRequired].Value)
            .Length(UsernameMinLength, UsernameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.UsernameLength, UsernameMinLength, UsernameMaxLength].Value)
            .Matches(UsernamePattern)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.UsernamePattern].Value);

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.PasswordRequired].Value)
            .MinimumLength(PasswordMinLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.PasswordTooShort, PasswordMinLength].Value)
            .Must(password => Encoding.UTF8.GetByteCount(password ?? string.Empty) <= PasswordMaxBytes)
            .WithMessage(_ => localizer[MessageKeys.Validation.Auth.PasswordTooLong, PasswordMaxBytes].Value);
    }
}
