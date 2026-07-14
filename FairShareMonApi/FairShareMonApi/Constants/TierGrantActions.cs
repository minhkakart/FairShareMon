namespace FairShareMonApi.Constants;

/// <summary>
/// Discriminator values stored in <c>tier_grants.action</c> (M11 Admin suite, OQ5). A
/// <see cref="Grant"/> row upgrades the user to Premium and records the offline payment amount; a
/// <see cref="Revoke"/> row downgrades to Free (amount 0). Only <see cref="Grant"/> rows count as
/// revenue (OQ14).
/// </summary>
public static class TierGrantActions
{
    public const string Grant = "GRANT";

    public const string Revoke = "REVOKE";
}
