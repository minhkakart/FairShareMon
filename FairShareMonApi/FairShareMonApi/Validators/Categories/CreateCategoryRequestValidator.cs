using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Categories;

/// <summary>
/// Category create rules (OQ2/OQ6): <c>Name</c> required (whitespace-only rejected) and at most 100
/// chars; <c>Color</c> required and a valid <c>#RRGGBB</c> hex string; <c>Icon</c> optional, at most
/// 50 chars. Uniqueness is enforced in the service (active-only, no partial index). The service
/// trims the name before persisting.
/// </summary>
public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    /// <summary>Hex color pattern <c>#RRGGBB</c> (OQ2).</summary>
    public const string ColorPattern = "^#[0-9A-Fa-f]{6}$";

    public CreateCategoryRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Category.NameRequired].Value)
            .MaximumLength(Category.NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Category.NameTooLong, Category.NameMaxLength].Value);

        RuleFor(request => request.Color)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Category.ColorRequired].Value)
            .Matches(ColorPattern).WithMessage(_ => localizer[MessageKeys.Validation.Category.ColorInvalid].Value);

        RuleFor(request => request.Icon)
            .MaximumLength(Category.IconMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Category.IconTooLong, Category.IconMaxLength].Value);
    }
}
