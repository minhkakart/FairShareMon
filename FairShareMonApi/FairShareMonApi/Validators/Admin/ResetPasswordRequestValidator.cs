using System.Text;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Validators.Auth;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// Admin-set temp password follows the same policy as registration (min 8 chars, max 72 bytes) - M11 OQ8.
/// </summary>
public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(request => request.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
            .MinimumLength(RegisterRequestValidator.PasswordMinLength)
            .WithMessage($"Mật khẩu mới phải có ít nhất {RegisterRequestValidator.PasswordMinLength} ký tự.")
            .Must(password => Encoding.UTF8.GetByteCount(password ?? string.Empty) <= RegisterRequestValidator.PasswordMaxBytes)
            .WithMessage($"Mật khẩu mới không được vượt quá {RegisterRequestValidator.PasswordMaxBytes} byte.");
    }
}
