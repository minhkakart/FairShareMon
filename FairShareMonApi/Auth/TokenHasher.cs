using System.Security.Cryptography;
using System.Text;

namespace FairShareMonApi.Auth;

/// <summary>
/// SHA-256 hashing of raw opaque tokens. The lowercase-hex digest (fixed 64 chars) is the only
/// form of a token ever persisted or cached - raw tokens leave the process exactly once.
/// </summary>
public static class TokenHasher
{
    public static string Sha256Hex(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
