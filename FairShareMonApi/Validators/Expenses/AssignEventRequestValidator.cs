using FairShareMonApi.Models.Expenses;
using FluentValidation;

namespace FairShareMonApi.Validators.Expenses;

/// <summary>
/// Assign-to-event rules (OQ4): <c>EventUuid</c> required. Ownership, open/closed state, and the
/// within-range check are enforced in the repository (9000/9001/9002).
/// </summary>
public class AssignEventRequestValidator : AbstractValidator<AssignEventRequest>
{
    public AssignEventRequestValidator()
    {
        RuleFor(request => request.EventUuid)
            .NotEmpty().WithMessage("UUID đợt chi tiêu không được để trống.");
    }
}
