using FairShareMonApi.Models.Auth;
using FluentValidation;

namespace FairShareMonApi.Validators.Auth;

/// <summary>Login only requires both fields present - credential checking is the service's job.</summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Username)
            .NotEmpty().WithMessage("Tên đăng nhập không được để trống.");

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.");
    }
}
