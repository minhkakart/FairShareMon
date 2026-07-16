using FairShareMonApi.Models.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Auth;

/// <summary>Login only requires both fields present - credential checking is the service's job.</summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Username)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.UsernameRequired].Value);

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Auth.PasswordRequired].Value);
    }
}
