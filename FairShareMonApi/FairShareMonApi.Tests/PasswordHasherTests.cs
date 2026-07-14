using FairShareMonApi.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for BCrypt password hashing. Behavioral tests use a low work factor (4) for
/// speed; the work-factor tests assert the cost embedded in the hash string (<c>$2?$NN$</c>).
/// </summary>
public class PasswordHasherTests
{
    private static PasswordHasher CreateHasher(int? workFactor = 4)
    {
        var settings = new Dictionary<string, string?>();
        if (workFactor is not null)
            settings["Auth:BcryptWorkFactor"] = workFactor.Value.ToString();

        return new PasswordHasher(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    [Fact]
    public void HashVerify_Roundtrip_Succeeds()
    {
        var hasher = CreateHasher();

        var hash = hasher.Hash("mật khẩu bí mật 123");

        Assert.StartsWith("$2", hash); // BCrypt format - never plaintext
        Assert.True(hasher.Verify("mật khẩu bí mật 123", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = CreateHasher();

        var hash = hasher.Hash("correct-password");

        Assert.False(hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var hasher = CreateHasher();

        var first = hasher.Hash("same-password");
        var second = hasher.Hash("same-password");

        Assert.NotEqual(first, second); // per-hash random salt
        Assert.True(hasher.Verify("same-password", first));
        Assert.True(hasher.Verify("same-password", second));
    }

    [Fact]
    public void Hash_ConfiguredWorkFactor_IsEmbeddedInHash()
    {
        var hasher = CreateHasher(workFactor: 6);

        var hash = hasher.Hash("any-password");

        Assert.Contains("$06$", hash);
    }

    [Fact]
    public void Hash_WithoutConfiguration_UsesDefaultWorkFactor11()
    {
        var hasher = CreateHasher(workFactor: null);

        var hash = hasher.Hash("any-password");

        Assert.Contains($"${PasswordHasher.DefaultWorkFactor:D2}$", hash);
    }

    [Fact]
    public void HashVerify_72BytePassword_Roundtrips()
    {
        // 72 bytes = the BCrypt truncation limit; the validators cap input at exactly this size.
        var hasher = CreateHasher();
        var password = new string('a', 72);

        var hash = hasher.Hash(password);

        Assert.True(hasher.Verify(password, hash));
        Assert.False(hasher.Verify(new string('b', 72), hash));
    }
}
