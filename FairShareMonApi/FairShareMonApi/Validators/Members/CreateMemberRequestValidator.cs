using FairShareMonApi.Models.Members;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
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

    public CreateMemberRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Member.NameRequired].Value)
            .MaximumLength(NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Member.NameTooLong, NameMaxLength].Value);
    }
}
