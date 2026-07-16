using DiDecoration.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Repositories;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Services.Api.Tiers;

/// <summary>
/// Dịch vụ tập trung kiểm tra hạng người dùng (The-ideal.md §3.11, §4 rule 9 - M10): giới hạn tạo mới
/// theo hạng Free (thành viên / đợt đang mở / phiếu mỗi tháng, đếm phía DB) và cổng tính năng "mở rộng"
/// chỉ dành cho Premium (ví &amp; QR). <b>Premium bỏ qua mọi giới hạn.</b> Giới hạn chỉ chặn TẠO MỚI, không
/// bao giờ đụng tới dữ liệu đã có (§4.9). Các con số giới hạn đọc từ cấu hình <c>Tiers:Free:</c>
/// (kiểu <c>Auth:</c>, OQ10a) với mặc định trong mã (25 / 10 / 200 - OQ1c). Không phụ thuộc vào các
/// feature service để tránh vòng DI (OQ9a).
/// </summary>
public interface ITierService
{
    /// <summary>Chặn khi tài khoản Free đã đạt số thành viên tối đa. Premium bỏ qua.</summary>
    Task EnsureCanCreateMemberAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>Chặn khi tài khoản Free đã đạt số đợt đang mở tối đa. Premium bỏ qua.</summary>
    Task EnsureCanCreateOpenEventAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>Chặn khi tài khoản Free đã đạt số phiếu chi tiêu tối đa trong tháng (theo múi giờ mặc định <c>App:DefaultTimeZone</c>). Premium bỏ qua.</summary>
    Task EnsureCanCreateExpenseAsync(string userUuid, CancellationToken cancellationToken = default);

    /// <summary>Cổng tính năng Premium: ném 403 <c>PremiumFeatureRequired</c> khi tài khoản không phải Premium.</summary>
    void EnsurePremiumFeature(string featureNameVi);
}

[ScopedService(typeof(ITierService))]
public sealed class TierService(
    IContextAuthenticated contextAuthenticated,
    IMemberRepository memberRepository,
    IEventRepository eventRepository,
    IExpenseRepository expenseRepository,
    IConfiguration configuration) : ITierService
{
    private readonly int _maxMembers = configuration.GetValue("Tiers:Free:MaxMembers", 25);
    private readonly int _maxOpenEvents = configuration.GetValue("Tiers:Free:MaxOpenEvents", 10);
    private readonly int _maxExpensesPerMonth = configuration.GetValue("Tiers:Free:MaxExpensesPerMonth", 200);

    // Premium bỏ qua mọi giới hạn; hạng thiếu/không rõ -> coi như Free (fail-safe, khớp mặc định DB).
    private bool IsPremium => contextAuthenticated.AuthenticatedUser?.Tier == UserTiers.Premium;

    public async Task EnsureCanCreateMemberAsync(string userUuid, CancellationToken cancellationToken = default)
    {
        if (IsPremium)
            return;

        var count = await memberRepository.CountActiveByUserAsync(userUuid, cancellationToken);
        if (count >= _maxMembers)
            throw new ErrorException(ErrorCodes.MemberLimitReached,
                $"Tài khoản Free chỉ được tạo tối đa {_maxMembers} thành viên. Nâng cấp Premium để bỏ giới hạn.");
    }

    public async Task EnsureCanCreateOpenEventAsync(string userUuid, CancellationToken cancellationToken = default)
    {
        if (IsPremium)
            return;

        var count = await eventRepository.CountOpenByUserAsync(userUuid, cancellationToken);
        if (count >= _maxOpenEvents)
            throw new ErrorException(ErrorCodes.OpenEventLimitReached,
                $"Tài khoản Free chỉ được có tối đa {_maxOpenEvents} đợt đang mở. Chốt bớt đợt hoặc nâng cấp Premium để bỏ giới hạn.");
    }

    public async Task EnsureCanCreateExpenseAsync(string userUuid, CancellationToken cancellationToken = default)
    {
        if (IsPremium)
            return;

        var (fromUtc, toUtc) = CurrentMonthUtcWindow(configuration);
        var count = await expenseRepository.CountByUserInRangeAsync(userUuid, fromUtc, toUtc, cancellationToken);
        if (count >= _maxExpensesPerMonth)
            throw new ErrorException(ErrorCodes.MonthlyExpenseLimitReached,
                $"Tài khoản Free chỉ được tạo tối đa {_maxExpensesPerMonth} phiếu chi tiêu mỗi tháng. Nâng cấp Premium để bỏ giới hạn.");
    }

    public void EnsurePremiumFeature(string featureNameVi)
    {
        if (IsPremium)
            return;

        throw new ErrorException(ErrorCodes.PremiumFeatureRequired,
            $"Tính năng {featureNameVi} chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.");
    }

    /// <summary>
    /// Cửa sổ UTC nửa mở <c>[from, to)</c> của tháng dương lịch hiện tại tính theo <b>múi giờ mặc định
    /// của ứng dụng</b> (<c>App:DefaultTimeZone</c>) - KHÔNG theo header <c>X-Time-Zone</c> của client
    /// (OQ4a/D4): hạn mức là chính sách phía máy chủ, nếu theo header thì người dùng có thể đổi múi giờ
    /// quanh ranh giới tháng để reset hạn mức. Lấy "bây giờ" (UTC) đổi sang múi giờ mặc định, về đầu
    /// tháng theo múi giờ đó, rồi quy về UTC cho cột <c>expense_time</c> (lưu UTC).
    /// </summary>
    private static (DateTime FromUtc, DateTime ToUtc) CurrentMonthUtcWindow(IConfiguration configuration)
    {
        var zone = TimeZoneResolver.GetDefaultZone(configuration);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(AppDateTime.Now, zone);
        var monthStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var nextMonthStartLocal = monthStartLocal.AddMonths(1);
        return (TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, zone),
                TimeZoneInfo.ConvertTimeToUtc(nextMonthStartLocal, zone));
    }
}
