namespace FairShareMonApi.Constants;

/// <summary>
/// Account status values stored in <c>users.status</c> (M11 Admin suite, OQ2). <see cref="Active"/>
/// is the registration default; <see cref="Disabled"/> blocks authentication (login is rejected and
/// existing tokens are revoked on disable). Reversible - enabling restores login.
/// </summary>
public static class UserStatuses
{
    public const string Active = "ACTIVE";

    public const string Disabled = "DISABLED";
}
