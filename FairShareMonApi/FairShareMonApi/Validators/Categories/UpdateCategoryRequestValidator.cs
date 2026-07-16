using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Categories;

/// <summary>
/// Category update follows the same field policy as create (OQ2/OQ6): <c>Name</c> required + max 100;
/// <c>Color</c> required + valid <c>#RRGGBB</c> hex; <c>Icon</c> optional + max 50. This endpoint
/// cannot change the default flag (OQ7). The service trims the name before persisting.
/// </summary>
public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Category.NameRequired].Value)
            .MaximumLength(Category.NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Category.NameTooLong, Category.NameMaxLength].Value);

        RuleFor(request => request.Color)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Category.ColorRequired].Value)
            .Matches(CreateCategoryRequestValidator.ColorPattern).WithMessage(_ => localizer[MessageKeys.Validation.Category.ColorInvalid].Value);

        RuleFor(request => request.Icon)
            .MaximumLength(Category.IconMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Category.IconTooLong, Category.IconMaxLength].Value);
    }
}
