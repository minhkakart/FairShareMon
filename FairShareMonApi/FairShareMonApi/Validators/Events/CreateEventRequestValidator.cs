using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Events;

/// <summary>
/// Event create rules (OQ9/OQ1): <c>Name</c> required (whitespace-only rejected) + max 200;
/// <c>Description</c> optional + max 1000; <c>StartDate</c>/<c>EndDate</c> required (non-default) and
/// <c>EndDate &gt;= StartDate</c> on the calendar day (the range is normalized to whole UTC days, so
/// the comparison is on the date part). An out-of-order range is a 1001 validation error, distinct
/// from the DB CHECK <c>ck_events_date_range</c> and the 9xxx business codes.
/// </summary>
public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator(IStringLocalizer<StringResources>? localizer = null)
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
