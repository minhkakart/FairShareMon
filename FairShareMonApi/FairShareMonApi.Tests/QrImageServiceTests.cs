using FairShareMonApi.Services.Api.Wallet;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="QrImageService"/> (M9 OQ2/OQ3, no DB). Proves the single-expense QR is
/// a PNG (QRCoder <c>PngByteQRCode</c>, magic bytes 89 50 4E 47) and the event composite is a PNG
/// (SkiaSharp, same magic) that grows with more members and renders Vietnamese labels without throwing
/// (the bundled Be Vietnam Pro font is loaded). Per-member composition count is asserted structurally in
/// <c>WalletQrServiceTests</c>; pixel-level QR counting isn't attempted here (brittle).
/// </summary>
public class QrImageServiceTests
{
    // PNG file signature: 89 50 4E 47 0D 0A 1A 0A.
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private readonly QrImageService _service = new();

    private const string SamplePayload =
        "00020101021238540010A00000072701240006970436011012345678900208QRIBFTTA53037045802VN6304ABCD";

    private static bool StartsWithPngMagic(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == PngMagic[0] && bytes[1] == PngMagic[1] && bytes[2] == PngMagic[2] && bytes[3] == PngMagic[3];

    [Fact]
    public void RenderSingle_ReturnsNonEmptyPngBytes()
    {
        var bytes = _service.RenderSingle(SamplePayload);

        Assert.NotEmpty(bytes);
        Assert.True(StartsWithPngMagic(bytes), "single QR must be a PNG (magic 89 50 4E 47)");
    }

    [Fact]
    public void RenderComposite_SingleItem_ReturnsPng()
    {
        var bytes = _service.RenderComposite([new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload)]);

        Assert.NotEmpty(bytes);
        Assert.True(StartsWithPngMagic(bytes), "composite must be a PNG (OQ3b)");
    }

    [Fact]
    public void RenderComposite_VietnameseLabels_RenderWithoutThrowing()
    {
        // The embedded font must handle the full diacritic set (headless-safe).
        var items = new[]
        {
            new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload),
            new QrCompositeItem("Trần Thị Bích Đào: 1.250.000đ", SamplePayload),
            new QrCompositeItem("Lê Cường: 75.000đ", SamplePayload)
        };

        var bytes = _service.RenderComposite(items);

        Assert.True(StartsWithPngMagic(bytes));
    }

    [Fact]
    public void RenderComposite_MoreMembers_ProducesTallerLargerImage()
    {
        var one = _service.RenderComposite([new QrCompositeItem("A: 1đ", SamplePayload)]);
        var three = _service.RenderComposite(
        [
            new QrCompositeItem("A: 1đ", SamplePayload),
            new QrCompositeItem("B: 2đ", SamplePayload),
            new QrCompositeItem("C: 3đ", SamplePayload)
        ]);

        // Vertical stacking: three cells encode to strictly more bytes than one.
        Assert.True(three.Length > one.Length, $"three-member composite ({three.Length}) should exceed one-member ({one.Length})");
    }

    [Fact]
    public void RenderComposite_EmptyItems_Throws()
    {
        Assert.Throws<ArgumentException>(() => _service.RenderComposite([]));
    }
}
