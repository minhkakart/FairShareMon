using FairShareMonApi.Constants;
using FairShareMonApi.Models.Admin;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>Role rule (M11 OQ9): <c>Role</c> phải là USER hoặc ADMIN.</summary>
public class SetRoleRequestValidator : AbstractValidator<SetRoleRequest>
{
    private static readonly string[] AllowedRoles = [UserRoles.User, UserRoles.Admin];

    public SetRoleRequestValidator()
    {
        RuleFor(request => request.Role)
            .NotEmpty().WithMessage("Vai trò không được để trống.")
            .Must(role => AllowedRoles.Contains(role))
            .WithMessage("Vai trò không hợp lệ. Chỉ chấp nhận USER hoặc ADMIN.");
    }
}
