namespace FairShareMonApi.Constants;

/// <summary>
/// Stable integer error codes returned in the <c>ApiResult</c> envelope. Values are part of the
/// public API contract - never renumber existing codes, only append new ones. The 1xxx block is
/// reserved for cross-cutting infrastructure codes; feature areas claim their own blocks in their
/// planning docs.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Unexpected server error (HTTP 500).</summary>
    public const int InternalError = 1000;

    /// <summary>Request payload failed validation (HTTP 400).</summary>
    public const int ValidationFailed = 1001;

    /// <summary>Missing, invalid, or expired credentials (HTTP 401).</summary>
    public const int Unauthorized = 1002;

    /// <summary>Resource not found - also used for ownership misses (HTTP 404, never 403).</summary>
    public const int NotFound = 1003;
}
