using FairShareMonApi.Models.Members;
using FluentValidation;

namespace FairShareMonApi.Validators.Members;

/// <summary>
/// Member display name: required (whitespace-only rejected) and at most 100 chars (OQ7). Names are
/// free-form - duplicates are allowed, so there is no uniqueness rule (OQ6). The service trims the
/// name before persisting.
/// </summary>
public class CreateMemberRequestValidator : AbstractValidator<CreateMemberRequest>
{
    public const int NameMaxLength = 100;

    public CreateMemberRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên thành viên không được để trống.")
            .MaximumLength(NameMaxLength)
            .WithMessage($"Tên thành viên không được vượt quá {NameMaxLength} ký tự.");
    }
}
