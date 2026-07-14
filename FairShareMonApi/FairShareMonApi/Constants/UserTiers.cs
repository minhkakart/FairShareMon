namespace FairShareMonApi.Constants;

/// <summary>
/// User tier values stored in <c>users.tier</c> (The-ideal.md §3.11). Free is the registration
/// default; tier enforcement (usage limits) is a later milestone.
/// </summary>
public static class UserTiers
{
    public const string Free = "FREE";

    public const string Premium = "PREMIUM";
}
