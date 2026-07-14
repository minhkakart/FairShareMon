using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;
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
    public CreateEventRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên đợt không được để trống.")
            .MaximumLength(Event.NameMaxLength)
            .WithMessage($"Tên đợt không được vượt quá {Event.NameMaxLength} ký tự.");

        RuleFor(request => request.Description)
            .MaximumLength(Event.DescriptionMaxLength)
            .WithMessage($"Mô tả đợt không được vượt quá {Event.DescriptionMaxLength} ký tự.");

        RuleFor(request => request.StartDate)
            .NotEmpty().WithMessage("Ngày bắt đầu không được để trống.");

        RuleFor(request => request.EndDate)
            .NotEmpty().WithMessage("Ngày kết thúc không được để trống.")
            .Must((request, endDate) => endDate.Date >= request.StartDate.Date)
            .WithMessage("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
    }
}
