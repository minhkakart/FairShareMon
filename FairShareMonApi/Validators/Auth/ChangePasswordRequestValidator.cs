using System.Text;
using FairShareMonApi.Models.Auth;
using FluentValidation;

namespace FairShareMonApi.Validators.Auth;

/// <summary>
/// New password follows the same policy as registration (min 8 chars, max 72 bytes - OQ3).
/// Reusing the current password is deliberately allowed (OQ3 decision) - no equality rule.
/// </summary>
public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty().WithMessage("Mật khẩu hiện tại không được để trống.");

        RuleFor(request => request.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
            .MinimumLength(RegisterRequestValidator.PasswordMinLength)
            .WithMessage($"Mật khẩu mới phải có ít nhất {RegisterRequestValidator.PasswordMinLength} ký tự.")
            .Must(password => Encoding.UTF8.GetByteCount(password ?? string.Empty) <= RegisterRequestValidator.PasswordMaxBytes)
            .WithMessage($"Mật khẩu mới không được vượt quá {RegisterRequestValidator.PasswordMaxBytes} byte.");
    }
}
