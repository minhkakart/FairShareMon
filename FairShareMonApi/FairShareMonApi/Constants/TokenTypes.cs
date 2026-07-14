namespace FairShareMonApi.Constants;

/// <summary>
/// Token type values stored in <c>auth_tokens.token_type</c>. Only <see cref="Access"/> tokens
/// authenticate requests; <see cref="Refresh"/> tokens are exchanged for new pairs.
/// </summary>
public static class TokenTypes
{
    public const string Access = "ACCESS";

    public const string Refresh = "REFRESH";
}
