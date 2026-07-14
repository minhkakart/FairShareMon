using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;
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
    public UpdateEventRequestValidator()
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
