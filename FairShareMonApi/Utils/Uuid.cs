using System.Security.Cryptography;

namespace FairShareMonApi.Utils;

/// <summary>
/// Manual UUIDv7 generator. .NET 8 has no <c>Guid.CreateVersion7()</c> (that API is .NET 9+ and
/// must never be used here). Layout per RFC 9562: 48-bit big-endian Unix timestamp in
/// milliseconds, 4-bit version (7), 12 random bits, 2-bit variant (10), 62 random bits.
/// Time-ordered, so the string form sorts chronologically. Ordering is guaranteed only across
/// millisecond boundaries - values generated within the same millisecond are purely random
/// relative to each other (there is no intra-millisecond monotonicity counter).
/// </summary>
public static class Uuid
{
    public static string NewV7()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70); // version 7
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 10

        return new Guid(bytes, bigEndian: true).ToString();
    }
}
