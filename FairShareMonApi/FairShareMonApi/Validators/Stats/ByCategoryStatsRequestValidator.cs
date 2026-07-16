using FairShareMonApi.Models.Stats;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Stats;

/// <summary>
/// By-category scope rules (OQ8): the same range rule as overview (<c>From &lt;= To</c> when both are
/// present), plus a mutual-exclusion rule - <c>EventUuid</c> may not be sent together with a time range
/// (<c>From</c>/<c>To</c>). Each mode alone is valid; both together is a 1001 validation error
/// (<c>error.fields</c> camelCase). Event ownership/existence is enforced downstream (404/9000).
/// </summary>
public class ByCategoryStatsRequestValidator : AbstractValidator<ByCategoryStatsRequest>
{
    public ByCategoryStatsRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.To)
            .Must((request, to) => !request.From.HasValue || !to.HasValue || request.From.Value <= to.Value)
            .WithMessage(_ => localizer[MessageKeys.Validation.Common.RangeInvalid].Value);

        RuleFor(request => request.EventUuid)
            .Must((request, eventUuid) => string.IsNullOrEmpty(eventUuid) || (!request.From.HasValue && !request.To.HasValue))
            .WithMessage(_ => localizer[MessageKeys.Validation.Stats.ScopeConflict].Value);
    }
}
