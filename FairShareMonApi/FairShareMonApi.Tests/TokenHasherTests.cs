using FairShareMonApi.Auth;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>Pure unit tests for SHA-256 token hashing (the only persisted form of a token).</summary>
public class TokenHasherTests
{
    [Fact]
    public void Sha256Hex_KnownVector_MatchesExpectedDigest()
    {
        // FIPS 180-2 test vector: SHA-256("abc").
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            TokenHasher.Sha256Hex("abc"));
    }

    [Fact]
    public void Sha256Hex_EmptyString_MatchesExpectedDigest()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            TokenHasher.Sha256Hex(string.Empty));
    }

    [Fact]
    public void Sha256Hex_AnyInput_Is64CharLowercaseHex()
    {
        var digest = TokenHasher.Sha256Hex("токен-テスト-token-🔑");

        Assert.Equal(64, digest.Length);
        Assert.Matches("^[0-9a-f]{64}$", digest);
    }

    [Fact]
    public void Sha256Hex_SameInput_IsDeterministic()
    {
        Assert.Equal(TokenHasher.Sha256Hex("same-raw-token"), TokenHasher.Sha256Hex("same-raw-token"));
    }

    [Fact]
    public void Sha256Hex_DifferentInputs_ProduceDifferentDigests()
    {
        Assert.NotEqual(TokenHasher.Sha256Hex("token-a"), TokenHasher.Sha256Hex("token-b"));
    }
}
