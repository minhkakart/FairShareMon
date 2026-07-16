using FairShareMonApi.Constants;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>Role rule (M11 OQ9): <c>Role</c> phải là USER hoặc ADMIN.</summary>
public class SetRoleRequestValidator : AbstractValidator<SetRoleRequest>
{
    private static readonly string[] AllowedRoles = [UserRoles.User, UserRoles.Admin];

    public SetRoleRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Role)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Admin.RoleRequired].Value)
            .Must(role => AllowedRoles.Contains(role))
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.RoleInvalid].Value);
    }
}
