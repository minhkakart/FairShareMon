using FairShareMonApi.Models.Members;
using FluentValidation;

namespace FairShareMonApi.Validators.Members;

/// <summary>
/// Rename follows the same name policy as create: required (whitespace-only rejected) and at most
/// 100 chars (OQ7). The service trims the name before persisting.
/// </summary>
public class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên thành viên không được để trống.")
            .MaximumLength(CreateMemberRequestValidator.NameMaxLength)
            .WithMessage($"Tên thành viên không được vượt quá {CreateMemberRequestValidator.NameMaxLength} ký tự.");
    }
}
