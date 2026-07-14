using System.Text;
using FairShareMonApi.Models.Auth;
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

    public RegisterRequestValidator()
    {
        RuleFor(request => request.Username)
            .NotEmpty().WithMessage("Tên đăng nhập không được để trống.")
            .Length(UsernameMinLength, UsernameMaxLength)
            .WithMessage($"Tên đăng nhập phải có từ {UsernameMinLength} đến {UsernameMaxLength} ký tự.")
            .Matches(UsernamePattern)
            .WithMessage("Tên đăng nhập chỉ được chứa chữ cái không dấu, chữ số và các ký tự _ . -");

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .MinimumLength(PasswordMinLength)
            .WithMessage($"Mật khẩu phải có ít nhất {PasswordMinLength} ký tự.")
            .Must(password => Encoding.UTF8.GetByteCount(password ?? string.Empty) <= PasswordMaxBytes)
            .WithMessage($"Mật khẩu không được vượt quá {PasswordMaxBytes} byte.");
    }
}
