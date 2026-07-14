using FairShareMonApi.Constants;
using FairShareMonApi.Models.Admin;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// Metrics rules (M11 OQ6): khi có cả hai mốc thì <c>From &lt;= To</c>; <c>Bucket</c> thuộc {day, month}.
/// Sai -&gt; 1001.
/// </summary>
public class AdminMetricsRequestValidator : AbstractValidator<AdminMetricsRequest>
{
    private static readonly string[] AllowedBuckets = [DashboardBuckets.Day, DashboardBuckets.Month];

    public AdminMetricsRequestValidator()
    {
        RuleFor(request => request.To)
            .Must((request, to) => !request.From.HasValue || !to.HasValue || request.From.Value <= to.Value)
            .WithMessage("Khoảng thời gian không hợp lệ: thời điểm bắt đầu phải trước hoặc bằng thời điểm kết thúc.");

        RuleFor(request => request.Bucket)
            .Must(bucket => AllowedBuckets.Contains(bucket))
            .WithMessage("Độ chia thời gian không hợp lệ. Chỉ chấp nhận day hoặc month.");
    }
}
