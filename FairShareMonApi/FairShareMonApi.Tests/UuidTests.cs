using System.Globalization;
using FairShareMonApi.Utils;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>Pure unit tests for the manual UUIDv7 generator (RFC 9562 layout).</summary>
public class UuidTests
{
    private const string CanonicalPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

    [Fact]
    public void NewV7_Generated_IsCanonical36CharLowercaseHex()
    {
        var uuid = Uuid.NewV7();

        Assert.Equal(36, uuid.Length);
        Assert.Matches(CanonicalPattern, uuid);
    }

    [Fact]
    public void NewV7_VersionField_Is7()
    {
        // Char index 14 is the version nibble: xxxxxxxx-xxxx-Mxxx-....
        for (var i = 0; i < 100; i++)
            Assert.Equal('7', Uuid.NewV7()[14]);
    }

    [Fact]
    public void NewV7_VariantField_IsRfc9562Variant10()
    {
        // Char index 19 is the variant nibble: variant bits 10xx => 8, 9, a or b.
        for (var i = 0; i < 100; i++)
            Assert.Contains(Uuid.NewV7()[19], "89ab");
    }

    [Fact]
    public void NewV7_TimestampField_MatchesCurrentUtcTime()
    {
        var beforeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var uuid = Uuid.NewV7();
        var afterMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // First 48 bits (12 hex chars around the first dash) are the big-endian Unix ms timestamp.
        var timestampMs = long.Parse(string.Concat(uuid.AsSpan(0, 8), uuid.AsSpan(9, 4)), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        Assert.InRange(timestampMs, beforeMs, afterMs);
    }

    [Fact]
    public void NewV7_SequentialGenerations_AreTimeOrderedAsStrings()
    {
        // The ms timestamp is the string prefix and lowercase hex sorts ordinally by value,
        // so UUIDs generated in later milliseconds must compare strictly greater.
        var previous = Uuid.NewV7();
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(2); // guarantee a later millisecond tick
            var current = Uuid.NewV7();

            Assert.True(
                string.CompareOrdinal(previous, current) < 0,
                $"Expected time-ordering: '{previous}' should sort before '{current}'.");

            previous = current;
        }
    }

    [Fact]
    public void NewV7_ManyGenerations_AreUnique()
    {
        var uuids = Enumerable.Range(0, 1000).Select(_ => Uuid.NewV7()).ToArray();

        Assert.Equal(uuids.Length, uuids.Distinct(StringComparer.Ordinal).Count());
    }
}
