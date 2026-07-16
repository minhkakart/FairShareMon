using FairShareMonApi.Constants;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// Metrics rules (M11 OQ6): khi có cả hai mốc thì <c>From &lt;= To</c>; <c>Bucket</c> thuộc {day, month}.
/// Sai -&gt; 1001.
/// </summary>
public class AdminMetricsRequestValidator : AbstractValidator<AdminMetricsRequest>
{
    private static readonly string[] AllowedBuckets = [DashboardBuckets.Day, DashboardBuckets.Month];

    public AdminMetricsRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.To)
            .Must((request, to) => !request.From.HasValue || !to.HasValue || request.From.Value <= to.Value)
            .WithMessage(_ => localizer[MessageKeys.Validation.Common.RangeInvalid].Value);

        RuleFor(request => request.Bucket)
            .Must(bucket => AllowedBuckets.Contains(bucket))
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.BucketInvalid].Value);
    }
}
