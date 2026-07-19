using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Services.Api.Wallet;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="LocalQrContentProvider"/> (no DB, no HTTP). Proves its key is "local"
/// and that its content is byte-identical to the hand-rolled <see cref="VietQrPayloadBuilder.Build"/> for
/// the same inputs — i.e. the default QR path is unchanged from M9. The account-holder name is unused by
/// the local TLV builder.
/// </summary>
public class LocalQrContentProviderTests
{
    private const string Bin = "970436";
    private const string Account = "0123456789";

    private readonly VietQrPayloadBuilder _builder = new();
    private readonly LocalQrContentProvider _provider;

    public LocalQrContentProviderTests() => _provider = new LocalQrContentProvider(_builder);

    [Fact]
    public void Key_IsLocal() => Assert.Equal("local", _provider.Key);

    [Theory]
    [InlineData(500_000, "Com trua")]
    [InlineData(0, null)]
    [InlineData(1234, "Ăn uống")]
    public async Task BuildContentAsync_IsByteIdenticalToTheLocalBuilder(long amount, string? addInfo)
    {
        var expected = _builder.Build(Bin, Account, amount, addInfo);

        var actual = await _provider.BuildContentAsync(
            new QrContentRequest(Bin, Account, "Nguyen Van A", amount, addInfo), CancellationToken.None);

        Assert.Equal(expected, actual);
    }
}
