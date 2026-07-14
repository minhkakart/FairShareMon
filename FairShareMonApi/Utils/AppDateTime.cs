namespace FairShareMonApi.Utils;

/// <summary>
/// Application clock (decided 2026-07-13, planning/user-authentication.md OQ10): <see cref="Now"/>
/// is <b>UTC</b> and the database stores UTC; presentation layers convert to UTC+7. Binds every
/// entity timestamp and all token-expiry/TTL computations.
/// </summary>
public static class AppDateTime
{
    public static DateTime Now => DateTime.UtcNow;
}
