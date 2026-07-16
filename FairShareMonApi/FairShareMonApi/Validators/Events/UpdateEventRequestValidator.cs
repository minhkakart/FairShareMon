using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Events;

/// <summary>
/// Event update follows the same field policy as create (OQ9/OQ1): <c>Name</c> required + max 200;
/// <c>Description</c> optional + max 1000; <c>StartDate</c>/<c>EndDate</c> required (non-default) and
/// <c>EndDate &gt;= StartDate</c> on the calendar day. Whether the new range excludes an
/// already-assigned expense (9003) and whether the event is closed (9001) are enforced in the
/// repository, not here.
/// </summary>
public class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Event.NameRequired].Value)
            .MaximumLength(Event.NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Event.NameTooLong, Event.NameMaxLength].Value);

        RuleFor(request => request.Description)
            .MaximumLength(Event.DescriptionMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Event.DescriptionTooLong, Event.DescriptionMaxLength].Value);

        RuleFor(request => request.StartDate)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Event.StartDateRequired].Value);

        RuleFor(request => request.EndDate)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Event.EndDateRequired].Value)
            .Must((request, endDate) => endDate.Date >= request.StartDate.Date)
            .WithMessage(_ => localizer[MessageKeys.Validation.Event.EndDateBeforeStart].Value);
    }
}
