using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Expenses;

/// <summary>
/// Assign-to-event rules (OQ4): <c>EventUuid</c> required. Ownership, open/closed state, and the
/// within-range check are enforced in the repository (9000/9001/9002).
/// </summary>
public class AssignEventRequestValidator : AbstractValidator<AssignEventRequest>
{
    public AssignEventRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.EventUuid)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Expense.EventUuidRequired].Value);
    }
}
