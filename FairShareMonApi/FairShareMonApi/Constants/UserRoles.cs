namespace FairShareMonApi.Constants;

/// <summary>
/// User role values stored in <c>users.role</c> (M11 Admin suite). <see cref="User"/> is the
/// registration default; <see cref="Admin"/> unlocks the admin management surface. An absent/unknown
/// role always resolves to <see cref="User"/> (fail-safe - never <see cref="Admin"/>).
/// </summary>
public static class UserRoles
{
    public const string User = "USER";

    public const string Admin = "ADMIN";
}
