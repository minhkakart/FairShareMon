using FairShareMonApi.Models.Members;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Members;

/// <summary>
/// Rename follows the same name policy as create: required (whitespace-only rejected) and at most
/// 100 chars (OQ7). The service trims the name before persisting.
/// </summary>
public class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Member.NameRequired].Value)
            .MaximumLength(CreateMemberRequestValidator.NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Member.NameTooLong, CreateMemberRequestValidator.NameMaxLength].Value);
    }
}
