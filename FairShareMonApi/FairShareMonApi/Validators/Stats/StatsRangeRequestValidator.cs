using FairShareMonApi.Models.Stats;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Stats;

/// <summary>
/// Overview range rule (OQ7): when both <c>From</c> and <c>To</c> are present, <c>From &lt;= To</c>.
/// Both bounds are optional (omit = all-time). An out-of-order range is a 1001 validation error
/// (<c>error.fields</c> camelCase <c>from</c>/<c>to</c>).
/// </summary>
public class StatsRangeRequestValidator : AbstractValidator<StatsRangeRequest>
{
    public StatsRangeRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.To)
            .Must((request, to) => !request.From.HasValue || !to.HasValue || request.From.Value <= to.Value)
            .WithMessage(_ => localizer[MessageKeys.Validation.Common.RangeInvalid].Value);
    }
}
