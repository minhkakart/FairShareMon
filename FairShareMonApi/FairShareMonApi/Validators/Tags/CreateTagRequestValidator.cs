using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Tags;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Tags;

/// <summary>
/// Tag name rules (OQ6): required (whitespace-only rejected) and at most 100 chars. Uniqueness is
/// enforced in the service (active-only). The service trims the name before persisting.
/// </summary>
public class CreateTagRequestValidator : AbstractValidator<CreateTagRequest>
{
    public CreateTagRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Tag.NameRequired].Value)
            .MaximumLength(Tag.NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Tag.NameTooLong, Tag.NameMaxLength].Value);
    }
}
