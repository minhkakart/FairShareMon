using FairShareMonApi.Models.Stats;
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
    public ByCategoryStatsRequestValidator()
    {
        RuleFor(request => request.To)
            .Must((request, to) => !request.From.HasValue || !to.HasValue || request.From.Value <= to.Value)
            .WithMessage("Khoảng thời gian không hợp lệ: thời điểm bắt đầu phải trước hoặc bằng thời điểm kết thúc.");

        RuleFor(request => request.EventUuid)
            .Must((request, eventUuid) => string.IsNullOrEmpty(eventUuid) || (!request.From.HasValue && !request.To.HasValue))
            .WithMessage("Chỉ được lọc theo đợt hoặc theo khoảng thời gian, không dùng đồng thời.");
    }
}
